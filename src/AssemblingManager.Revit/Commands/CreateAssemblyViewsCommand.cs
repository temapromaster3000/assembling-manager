using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AssemblingManager.Core.Models;
using AssemblingManager.Revit.Services;
using AssemblingManager.Revit.Views;

namespace AssemblingManager.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateAssemblyViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApplication = commandData.Application;
            UIDocument uiDocument = uiApplication.ActiveUIDocument;
            Document document = uiDocument.Document;

            List<AssemblyInstance> assemblies = new FilteredElementCollector(document)
                .OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .ToList();

            if (assemblies.Count == 0)
            {
                MessageBox.Show("В модели не найдены сборки.", "Assembling Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                return Result.Cancelled;
            }

            AssemblyService assemblyService = new AssemblyService();
            Dictionary<AssemblyInstance, ICollection<ElementId>> assemblyElements = new Dictionary<AssemblyInstance, ICollection<ElementId>>();
            HashSet<Category> allCategories = new HashSet<Category>();
            List<ElementId> allElementIds = new List<ElementId>();

            foreach (AssemblyInstance assembly in assemblies)
            {
                ICollection<ElementId> elementIds = assemblyService.CollectAssemblyElements(document, assembly);
                assemblyElements[assembly] = elementIds;
                allElementIds.AddRange(elementIds);

                foreach (ElementId elementId in elementIds)
                {
                    Element element = document.GetElement(elementId);
                    if (element != null && element.Category != null)
                    {
                        allCategories.Add(element.Category);
                    }
                }
            }

            ViewCreationOptions options = null;

            while (true)
            {
                MainWindow window = new MainWindow(
                    assemblies.Count,
                    allCategories.ToList(),
                    allElementIds,
                    document,
                    options);
                bool? dialogResult = window.ShowDialog();

                if (dialogResult != true)
                {
                    return Result.Cancelled;
                }

                options = window.Options;

                ViewService viewService = new ViewService();
                List<ViewConflictItem> conflicts = viewService.FindExistingViewConflicts(document, assemblies, options);

                ViewConflictResolution resolution = new ViewConflictResolution();

                if (conflicts.Count > 0)
                {
                    List<string> activeViewConflicts = GetActiveViewConflicts(uiDocument, conflicts);
                    if (activeViewConflicts.Count > 0)
                    {
                        string messageText = "Нельзя заменить виды, которые сейчас открыты:\n\n" +
                            string.Join("\n", activeViewConflicts) +
                            "\n\nЗакройте эти виды и попробуйте снова.";
                        MessageBox.Show(messageText, "Assembling Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                        continue;
                    }

                    ConflictDialog conflictDialog = new ConflictDialog(conflicts);
                    bool? conflictResult = conflictDialog.ShowDialog();

                    if (conflictResult != true)
                    {
                        continue;
                    }

                    activeViewConflicts = GetActiveViewConflicts(uiDocument, conflictDialog.ConflictItems.Where(i => i.Replace).ToList());
                    if (activeViewConflicts.Count > 0)
                    {
                        string messageText = "Нельзя заменить виды, которые сейчас открыты:\n\n" +
                            string.Join("\n", activeViewConflicts) +
                            "\n\nЗакройте эти виды и попробуйте снова.";
                        MessageBox.Show(messageText, "Assembling Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                        continue;
                    }

                    resolution.Items = conflictDialog.ConflictItems;
                }

                Stopwatch stopwatch = Stopwatch.StartNew();
                ViewCreationResult result;

                using (TransactionGroup transactionGroup = new TransactionGroup(document, "Assembling Manager"))
                {
                    transactionGroup.Start();

                    try
                    {
                        using (Transaction transaction = new Transaction(document, "Create views and filters"))
                        {
                            FailureHandlingOptions failureOptions = transaction.GetFailureHandlingOptions();
                            failureOptions.SetFailuresPreprocessor(new FailurePreprocessor());
                            transaction.SetFailureHandlingOptions(failureOptions);

                            transaction.Start();

                            OrchestratorService orchestrator = new OrchestratorService();
                            result = orchestrator.GenerateViews(document, uiApplication.Application, options, resolution);

                            transaction.Commit();
                        }

                        transactionGroup.Assimilate();
                    }
                    catch (Exception ex)
                    {
                        transactionGroup.RollBack();
                        message = ex.Message;
                        return Result.Failed;
                    }
                }

                stopwatch.Stop();
                result.Elapsed = stopwatch.Elapsed;

                ReportDialog reportDialog = new ReportDialog(result);
                reportDialog.ShowDialog();

                return Result.Succeeded;
            }
        }
        private List<string> GetActiveViewConflicts(UIDocument uiDocument, List<ViewConflictItem> conflictsToReplace)
        {
            View activeView = uiDocument.ActiveView;
            if (activeView == null)
            {
                return new List<string>();
            }

            List<string> result = new List<string>();

            foreach (ViewConflictItem item in conflictsToReplace)
            {
                if (item.ViewName == activeView.Name)
                {
                    result.Add(item.ViewName);
                }
            }

            return result;
        }
    }
}
