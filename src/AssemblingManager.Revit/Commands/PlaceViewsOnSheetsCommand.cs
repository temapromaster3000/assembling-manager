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
        private const string SignalSheetSuffix = " (не размещенное)";

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

            if (dialogResult != true
                || dialog.SelectedGroupNode == null
                || dialog.SelectedMasterSheet == null
                || dialog.SelectedSheetGroupNode == null)
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

            HashSet<string> groupSheetNumbers = new HashSet<string>(
                dialog.SelectedSheetGroupNode.GetAllSheets()
                    .Select(s => s.SheetNumber?.Trim() ?? string.Empty)
                    .Where(n => !string.IsNullOrEmpty(n)),
                StringComparer.OrdinalIgnoreCase);

            SheetService.SheetPlacementPlan plan = sheetService.BuildPlacementPlan(
                document,
                objects,
                masterSheet,
                groupSheetNumbers,
                SignalSheetSuffix);

            if (plan.Sheets.Count == 0)
            {
                Logger.Info("Placement plan is empty: all views are already placed.");
                TaskDialog.Show(Constants.PluginName, "Создавать нечего: все виды из выбранной группы уже размещены на листах.");
                return Result.Cancelled;
            }

            PlacementPreviewDialog previewDialog = new PlacementPreviewDialog(plan);
            bool? previewResult = previewDialog.ShowDialog();

            if (previewResult != true)
            {
                Logger.Info("User cancelled the placement preview.");
                return Result.Cancelled;
            }

            SheetPlacementResult result = new SheetPlacementResult
            {
                FullyPlacedGroupsCount = plan.FullyPlacedGroupsCount
            };
            Stopwatch stopwatch = Stopwatch.StartNew();

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

                        foreach (SheetService.PlannedSheetInfo item in plan.Sheets)
                        {
                            ViewSheet newSheet = sheetService.CreateSheetCopy(document, masterSheet, item.SheetName, item.SheetNumber, groupParameterIds);

                            if (item.IsSignal)
                            {
                                result.SignalSheetsCount++;
                                Logger.Info($"Created signal sheet '{item.SheetNumber}' ('{item.SheetName}') for unfinished views of object '{item.ObjectName}'.");
                            }
                            else
                            {
                                result.CreatedSheetsCount++;
                                Logger.Info($"Created sheet '{item.SheetNumber}' ('{item.SheetName}') for object '{item.ObjectName}'.");
                            }

                            ObjectViewGroup unplacedGroup = BuildUnplacedGroup(document, item.ObjectName, item.UnplacedViewIds);
                            sheetService.PlaceObjectViewsOnSheet(document, newSheet, unplacedGroup, placedViewIds, null, result.Warnings);
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
            Logger.Info($"Created: {result.CreatedSheetsCount}, Signal sheets: {result.SignalSheetsCount}, Fully placed groups: {result.FullyPlacedGroupsCount}, Warnings: {result.Warnings.Count}");

            ShowSummary(result);

            return Result.Succeeded;
        }

        private static ObjectViewGroup BuildUnplacedGroup(Document doc, string objectName, List<ElementId> unplacedViewIds)
        {
            ObjectViewGroup result = new ObjectViewGroup { ObjectName = objectName };

            foreach (ElementId viewId in unplacedViewIds)
            {
                View view = doc.GetElement(viewId) as View;
                if (view == null)
                {
                    continue;
                }

                if (view is ViewPlan)
                {
                    result.Plans.Add(view);
                }
                else if (view is View3D)
                {
                    result.Views3D.Add(view);
                }
                else if (view is ViewSection)
                {
                    result.Sections.Add(view);
                }
                else if (view is ViewSchedule)
                {
                    result.Schedules.Add(view);
                }
                else
                {
                    result.Unsupported.Add(view);
                }
            }

            return result;
        }

        private static void ShowSummary(SheetPlacementResult result)
        {
            const int maxWarnings = 25;

            TaskDialog taskDialog = new TaskDialog(Constants.PluginName);
            string signalText = result.SignalSheetsCount > 0
                ? $", сигнальных листов: {result.SignalSheetsCount}"
                : string.Empty;
            string fullyPlacedText = result.FullyPlacedGroupsCount > 0
                ? $", полностью размещено групп: {result.FullyPlacedGroupsCount}"
                : string.Empty;

            taskDialog.MainInstruction =
                $"Создано листов: {result.CreatedSheetsCount}{signalText}{fullyPlacedText}.";

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
