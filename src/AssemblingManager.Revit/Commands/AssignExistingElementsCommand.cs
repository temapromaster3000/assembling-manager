using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using AssemblingManager.Core.Models;
using AssemblingManager.Revit.Services;
using AssemblingManager.Revit.Views;

namespace AssemblingManager.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AssignExistingElementsCommand : IExternalCommand
    {
        private const string PluginParameterName = "AssemblyParameter";
        private const string GroupingParameterName = "ADSK_Группирование";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApplication = commandData.Application;
            UIDocument uiDocument = uiApplication.ActiveUIDocument;
            Document document = uiDocument.Document;

            Logger.Info("=== AssignExistingElementsCommand started ===");
            Logger.Info($"Log file: {Logger.GetLogFilePath()}");
            Logger.Info($"Document: {document.Title}");

            List<AssemblyInstance> assemblies = new FilteredElementCollector(document)
                .OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .ToList();

            if (assemblies.Count == 0)
            {
                Logger.Warn("No assemblies found. Command cancelled.");
                MessageBox.Show("В модели не найдены сборки.", "Assembling Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                return Result.Cancelled;
            }

            IList<Reference> pickedReferences;

            try
            {
                pickedReferences = uiDocument.Selection.PickObjects(
                    ObjectType.Element,
                    "Выберите существующие элементы (рамкой или через Ctrl), затем нажмите «Готово».");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                Logger.Info("Element selection cancelled by user.");
                return Result.Cancelled;
            }

            List<ElementId> selectedIds = pickedReferences
                .Select(r => r.ElementId)
                .Distinct()
                .ToList();

            if (selectedIds.Count == 0)
            {
                Logger.Info("No elements picked. Command cancelled.");
                MessageBox.Show("Не выбрано ни одного элемента.", "Assembling Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                return Result.Cancelled;
            }

            Logger.Info($"User picked {selectedIds.Count} elements.");

            AssemblyService assemblyService = new AssemblyService();
            HashSet<ElementId> elementIds = assemblyService.CollectElementsWithNested(document, selectedIds);
            int nestedCount = elementIds.Count - selectedIds.Count;
            Logger.Info($"Total elements including nested: {elementIds.Count} (nested: {nestedCount}).");

            HashSet<Category> assemblyCategories = CollectAssemblyCategories(document, assemblies, assemblyService);
            Logger.Info($"Collected {assemblyCategories.Count} categories from all assemblies.");

            HashSet<ElementId> allowedElementIds = new HashSet<ElementId>();
            List<string> disallowedCategoryNames = new List<string>();

            foreach (ElementId elementId in elementIds)
            {
                Element element = document.GetElement(elementId);

                if (element?.Category != null && assemblyCategories.Contains(element.Category))
                {
                    allowedElementIds.Add(elementId);
                }
                else
                {
                    disallowedCategoryNames.Add(element?.Category != null ? element.Category.Name : "без категории");
                }
            }

            disallowedCategoryNames = disallowedCategoryNames
                .Distinct()
                .OrderBy(n => n)
                .ToList();
            int disallowedCount = elementIds.Count - allowedElementIds.Count;

            if (disallowedCount > 0)
            {
                Logger.Warn($"Disallowed categories among picked elements: {string.Join(", ", disallowedCategoryNames)}; {disallowedCount} elements will be skipped.");

                MessageBox.Show(
                    "Категория добавляемого элемента не относится к сборкам.\n\n" +
                    $"Пропущены категории: {string.Join(", ", disallowedCategoryNames)} (элементов: {disallowedCount}).\n\n" +
                    "Добавлять существующее можно только для категорий, которые встречаются в сборках.",
                    "Добавить существующее",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            if (allowedElementIds.Count == 0)
            {
                Logger.Info("All picked elements have disallowed categories. Command cancelled.");
                MessageBox.Show(
                    "Ни один выбранный элемент не относится к категориям, встречающимся в сборках. Добавление отменено.",
                    "Добавить существующее",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return Result.Cancelled;
            }

            nestedCount = allowedElementIds.Count - selectedIds.Count(id => allowedElementIds.Contains(id));
            Logger.Info($"Elements allowed for adding: {allowedElementIds.Count} (nested: {nestedCount}).");

            AssignExistingDialog dialog = new AssignExistingDialog(document, assemblies, allowedElementIds.Count, nestedCount);
            bool? dialogResult = dialog.ShowDialog();

            if (dialogResult != true)
            {
                Logger.Info("User cancelled the dialog.");
                return Result.Cancelled;
            }

            AssemblyInstance assembly = dialog.SelectedAssembly;
            string parameterName = dialog.UseGroupingParameter ? GroupingParameterName : PluginParameterName;
            Logger.Info($"Selected assembly '{assembly.Name}', parameter '{parameterName}'.");

            ParameterService parameterService = new ParameterService();

            HashSet<string> existingValues = parameterService.GetDistinctParameterValues(document, parameterName, allowedElementIds);

            if (existingValues.Count > 0)
            {
                int filledCount = parameterService.CountExistingValues(document, parameterName, allowedElementIds);

                List<string> valueList = existingValues.Take(5).ToList();
                string valuesText = string.Join(", ", valueList);
                if (existingValues.Count > 5)
                {
                    valuesText += $" и ещё {existingValues.Count - 5}";
                }

                string confirmText = $"У {filledCount} из выбранных элементов параметр «{parameterName}» уже заполнен ({valuesText}).\n\n" +
                    $"Перезаписать значением «{assembly.Name}»?";

                MessageBoxResult confirm = MessageBox.Show(confirmText, "Добавить существующее", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                {
                    Logger.Info("User declined overwriting existing values. Command cancelled.");
                    return Result.Cancelled;
                }
            }

            AssignExistingResult result;

            using (TransactionGroup transactionGroup = new TransactionGroup(document, "Assembling Manager"))
            {
                transactionGroup.Start();
                Logger.Info("TransactionGroup started.");

                try
                {
                    using (Transaction transaction = new Transaction(document, "Добавить существующее"))
                    {
                        FailureHandlingOptions failureOptions = transaction.GetFailureHandlingOptions();
                        failureOptions.SetFailuresPreprocessor(new FailurePreprocessor());
                        transaction.SetFailureHandlingOptions(failureOptions);

                        transaction.Start();
                        Logger.Info("Transaction started.");

                        bool parameterExisted = true;
                        ElementId parameterId;

                        if (dialog.UseGroupingParameter)
                        {
                            parameterId = parameterService.GetParameterByName(document, GroupingParameterName);

                            if (parameterId == null)
                            {
                                throw new InvalidOperationException($"Параметр '{GroupingParameterName}' не найден в проекте.");
                            }
                        }
                        else
                        {
                            parameterId = parameterService.GetParameterByName(document, PluginParameterName);

                            if (parameterId == null)
                            {
                                parameterExisted = false;
                                Logger.Info($"Parameter '{PluginParameterName}' not found, creating with {assemblyCategories.Count} assembly categories.");
                                parameterId = parameterService.GetOrCreateParameter(document, uiApplication.Application, assemblyCategories.ToList());
                            }
                        }

                        AssignExistingService service = new AssignExistingService();
                        result = service.Execute(document, parameterId, parameterName, assembly, allowedElementIds.ToList(), assemblyCategories, nestedCount, parameterExisted);
                        result.SkippedDisallowedCount = disallowedCount;
                        result.SkippedDisallowedCategoryNames = disallowedCategoryNames;

                        transaction.Commit();
                        Logger.Info("Transaction committed.");
                    }

                    transactionGroup.Assimilate();
                    Logger.Info("TransactionGroup assimilated.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Exception during execution: {ex}");
                    transactionGroup.RollBack();
                    Logger.Info("TransactionGroup rolled back.");
                    message = ex.Message;
                    return Result.Failed;
                }
            }

            Logger.Info($"=== AssignExistingElementsCommand finished: written {result.WrittenCount} of {result.TotalElementCount} ===");

            MessageBox.Show(BuildSummary(result), "Добавить существующее", MessageBoxButton.OK, MessageBoxImage.Information);

            return Result.Succeeded;
        }

        private static string BuildSummary(AssignExistingResult result)
        {
            List<string> lines = new List<string>
            {
                $"Сборка: {result.AssemblyName}",
                $"Параметр: {result.ParameterName}",
                string.Empty,
                $"Записано значений: {result.WrittenCount} из {result.TotalElementCount} (в т.ч. вложенных: {result.NestedCount})."
            };

            if (result.SkippedReadOnlyCount > 0)
            {
                lines.Add($"Пропущено (параметр только для чтения): {result.SkippedReadOnlyCount}.");
            }

            if (result.SkippedNoParameterCount > 0)
            {
                lines.Add($"Пропущено (параметр недоступен для категории): {result.SkippedNoParameterCount}.");
            }

            if (result.SkippedDisallowedCount > 0)
            {
                lines.Add($"Пропущено (категория не из сборок): {result.SkippedDisallowedCount} ({string.Join(", ", result.SkippedDisallowedCategoryNames)}).");
            }

            if (result.AddedParameterCategories.Count > 0)
            {
                lines.Add($"Категории сборок добавлены в привязку параметра: {string.Join(", ", result.AddedParameterCategories)}.");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static HashSet<Category> CollectAssemblyCategories(Document doc, List<AssemblyInstance> assemblies, AssemblyService assemblyService)
        {
            HashSet<Category> categories = new HashSet<Category>();

            foreach (AssemblyInstance assembly in assemblies)
            {
                foreach (ElementId elementId in assemblyService.CollectAssemblyElements(doc, assembly))
                {
                    Element element = doc.GetElement(elementId);
                    if (element != null && element.Category != null)
                    {
                        categories.Add(element.Category);
                    }
                }
            }

            return categories;
        }
    }
}
