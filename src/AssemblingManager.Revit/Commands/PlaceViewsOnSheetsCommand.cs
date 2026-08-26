using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AssemblingManager.Core.Common;
using AssemblingManager.Core.Models;
using AssemblingManager.Revit.Services;
using AssemblingManager.Revit.Views;

namespace AssemblingManager.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceViewsOnSheetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApplication = commandData.Application;
            UIDocument uiDocument = uiApplication.ActiveUIDocument;

            if (uiDocument == null)
            {
                TaskDialog.Show(Constants.PluginName, "Необходимо открыть модель.");
                return Result.Cancelled;
            }

            Document document = uiDocument.Document;

            Logger.Info("=== PlaceViewsOnSheetsCommand started ===");
            Logger.Info($"Log file: {Logger.GetLogFilePath()}");
            Logger.Info($"Document: {document.Title}");

            BrowserGroupService browserGroupService = new BrowserGroupService();
            List<ViewGroupNode> groupRoots;

            try
            {
                groupRoots = browserGroupService.BuildGroupTree(document);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to build browser group tree: {ex}");
                TaskDialog.Show(Constants.PluginName, $"Не удалось прочитать структуру диспетчера видов: {ex.Message}");
                return Result.Failed;
            }

            int totalViews = groupRoots.Sum(r => r.CollectViewIds().Count);
            if (totalViews == 0)
            {
                Logger.Warn("No browser views found. Command cancelled.");
                TaskDialog.Show(Constants.PluginName, "В модели не найдено видов для размещения.");
                return Result.Cancelled;
            }

            SheetService sheetService = new SheetService();
            List<SheetGroupNode> sheetRoots = browserGroupService.BuildSheetTree(document);
            int totalSheets = sheetRoots.Sum(r => r.CountSheets());

            if (totalSheets == 0)
            {
                Logger.Warn("No sheets found. Command cancelled.");
                TaskDialog.Show(Constants.PluginName, "В модели нет листов — нечего копировать. Создайте лист-образец и повторите.");
                return Result.Cancelled;
            }

            PlaceViewsDialog dialog = new PlaceViewsDialog(document, groupRoots, sheetRoots, sheetService);
            bool? dialogResult = dialog.ShowDialog();

            if (dialogResult != true || dialog.SelectedGroupNode == null || dialog.SelectedMasterSheet == null)
            {
                Logger.Info("User cancelled the place-views dialog.");
                return Result.Cancelled;
            }

            ViewSheet masterSheet = dialog.SelectedMasterSheet;
            List<ObjectViewGroup> objects = sheetService.GroupViewsByObject(document, dialog.SelectedGroupNode.ViewIds);

            Logger.Info($"Master sheet: '{masterSheet.SheetNumber} — {masterSheet.Name}'. Objects: {objects.Count}.");

            if (objects.Count == 0)
            {
                TaskDialog.Show(Constants.PluginName, "В выбранной группе не нашлось видов, из которых можно собрать объекты.");
                return Result.Cancelled;
            }

            List<SheetConflictItem> conflicts = sheetService.FindSheetConflicts(document, objects, masterSheet);
            Dictionary<string, bool> replaceByObject = new Dictionary<string, bool>();

            if (conflicts.Count > 0)
            {
                Logger.Info($"Found {conflicts.Count} sheet name conflicts.");

                SheetConflictDialog conflictDialog = new SheetConflictDialog(conflicts);
                bool? conflictResult = conflictDialog.ShowDialog();

                if (conflictResult != true)
                {
                    Logger.Info("User cancelled the conflict dialog.");
                    return Result.Cancelled;
                }

                foreach (SheetConflictItem item in conflictDialog.ConflictItems)
                {
                    replaceByObject[item.ObjectName] = item.Replace;
                }
            }

            SheetPlacementResult result = new SheetPlacementResult();
            Stopwatch stopwatch = Stopwatch.StartNew();

            HashSet<string> occupiedNumbers = sheetService.GetAllSheetNumbers(document);
            HashSet<ElementId> placedViewIds = sheetService.GetViewsPlacedOnSheets(document);
            List<ElementId> groupParameterIds = sheetService.GetSheetGroupParameterIds(document, masterSheet);

            using (TransactionGroup transactionGroup = new TransactionGroup(document, "Assembling Manager: листы объектов"))
            {
                transactionGroup.Start();
                Logger.Info("TransactionGroup started.");

                try
                {
                    using (Transaction transaction = new Transaction(document, "Разместить виды на листах"))
                    {
                        FailureHandlingOptions failureOptions = transaction.GetFailureHandlingOptions();
                        failureOptions.SetFailuresPreprocessor(new FailurePreprocessor());
                        transaction.SetFailureHandlingOptions(failureOptions);

                        transaction.Start();
                        Logger.Info("Transaction started.");

                        foreach (ObjectViewGroup group in objects)
                        {
                            List<ViewSheet> existing = sheetService.FindSheetsByName(document, group.ObjectName, masterSheet);
                            bool replace = existing.Count > 0 && replaceByObject.TryGetValue(group.ObjectName, out bool replaceValue) && replaceValue;

                            if (existing.Count > 0 && !replace)
                            {
                                result.SkippedObjectsCount++;
                                Logger.Info($"Object '{group.ObjectName}' skipped: existing sheet(s) kept as is.");
                                continue;
                            }

                            if (existing.Count > 0)
                            {
                                ViewSheet targetSheet = existing[0];
                                List<ViewSheet> duplicates = existing.Skip(1).ToList();

                                Dictionary<ElementId, XYZ> positionHints = new Dictionary<ElementId, XYZ>();
                                sheetService.PrepareSheetsForPlacement(
                                    document,
                                    targetSheet,
                                    duplicates,
                                    group,
                                    placedViewIds,
                                    positionHints,
                                    result.Warnings);

                                sheetService.PlaceObjectViewsOnSheet(document, targetSheet, group, placedViewIds, positionHints, result.Warnings);
                                result.UpdatedSheetsCount++;

                                Logger.Info($"Updated sheet '{targetSheet.SheetNumber}' for object '{group.ObjectName}'.");
                            }
                            else
                            {
                                string newNumber = sheetService.GenerateNextSheetNumber(masterSheet, occupiedNumbers);
                                ViewSheet newSheet = sheetService.CreateSheetCopy(document, masterSheet, group.ObjectName, newNumber, groupParameterIds);
                                result.CreatedSheetsCount++;

                                Logger.Info($"Created sheet '{newNumber}' for object '{group.ObjectName}'.");

                                sheetService.PlaceObjectViewsOnSheet(document, newSheet, group, placedViewIds, null, result.Warnings);
                            }
                        }

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

            stopwatch.Stop();
            result.Elapsed = stopwatch.Elapsed;

            Logger.Info($"=== PlaceViewsOnSheetsCommand finished: {result.Elapsed.TotalSeconds:F2} s ===");
            Logger.Info($"Created: {result.CreatedSheetsCount}, Updated: {result.UpdatedSheetsCount}, Skipped: {result.SkippedObjectsCount}, Warnings: {result.Warnings.Count}");

            ShowSummary(result);

            return Result.Succeeded;
        }

        private static void ShowSummary(SheetPlacementResult result)
        {
            const int maxWarnings = 25;

            TaskDialog taskDialog = new TaskDialog(Constants.PluginName);
            taskDialog.MainInstruction = $"Создано листов: {result.CreatedSheetsCount}, обновлено: {result.UpdatedSheetsCount}, пропущено объектов: {result.SkippedObjectsCount}.";

            if (result.Warnings.Count == 0)
            {
                taskDialog.MainContent = $"Предупреждений нет. Время: {result.Elapsed.TotalSeconds:F1} с.";
            }
            else
            {
                string text = "Предупреждения:\n" + string.Join("\n", result.Warnings.Take(maxWarnings));

                if (result.Warnings.Count > maxWarnings)
                {
                    text += $"\n... и ещё {result.Warnings.Count - maxWarnings} (полный список в логе: {Logger.GetLogFilePath()})";
                }

                taskDialog.MainContent = text;
            }

            taskDialog.Show();
        }
    }
}
