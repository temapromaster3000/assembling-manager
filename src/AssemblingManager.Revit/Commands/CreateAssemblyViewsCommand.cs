using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

            int assemblyCount = new FilteredElementCollector(document)
                .OfClass(typeof(AssemblyInstance))
                .GetElementCount();

            ViewCreationOptions options = null;

            while (true)
            {
                MainWindow window = new MainWindow(assemblyCount, options);
                bool? dialogResult = window.ShowDialog();

                if (dialogResult != true)
                {
                    return Result.Cancelled;
                }

                options = window.Options;

                ViewService viewService = new ViewService();
                List<AssemblyInstance> assemblies = new FilteredElementCollector(document)
                    .OfClass(typeof(AssemblyInstance))
                    .Cast<AssemblyInstance>()
                    .ToList();

                List<ViewConflictItem> conflicts = viewService.FindExistingViewConflicts(document, assemblies, options);

                ViewConflictResolution resolution = new ViewConflictResolution();

                if (conflicts.Count > 0)
                {
                    ConflictDialog conflictDialog = new ConflictDialog(conflicts);
                    bool? conflictResult = conflictDialog.ShowDialog();

                    if (conflictResult != true)
                    {
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
    }
}
