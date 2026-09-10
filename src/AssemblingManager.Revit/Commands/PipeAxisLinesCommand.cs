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
    public class PipeAxisLinesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApplication = commandData.Application;
            Document document = uiApplication.ActiveUIDocument?.Document;

            if (document == null)
            {
                TaskDialog.Show(Constants.PluginName, "Необходимо открыть модель.");
                return Result.Cancelled;
            }

            Logger.Info("=== PipeAxisLinesCommand started ===");
            Logger.Info($"Log file: {Logger.GetLogFilePath()}");
            Logger.Info($"Document: {document.Title}");

            List<AssemblyInstance> assemblies = new FilteredElementCollector(document)
                .OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .ToList();

            if (assemblies.Count == 0)
            {
                Logger.Warn("No assemblies found. Command cancelled.");
                TaskDialog.Show(Constants.PluginName, "В модели не найдены сборки.");
                return Result.Cancelled;
            }

            Logger.Info($"Found {assemblies.Count} assemblies.");

            PipeAxisService service = new PipeAxisService();

            List<GraphicsStyle> lineStyles = service.GetLineStyles(document);
            if (lineStyles.Count == 0)
            {
                Logger.Warn("No line styles found. Command cancelled.");
                TaskDialog.Show(
                    Constants.PluginName,
                    "В проекте не найдено ни одного стиля линий.\n\n" +
                    "Создайте стиль в «Управление → Дополнительные настройки → Стили линий» и повторите запуск.");
                return Result.Cancelled;
            }

            PipeAxisSettings presetSettings = PipeAxesPresetStorage.ReadSettings(document);

            PipeAxesDialog dialog = new PipeAxesDialog(assemblies, presetSettings, lineStyles);
            bool? dialogResult = dialog.ShowDialog();

            if (dialogResult != true || dialog.SelectedAssemblies == null || dialog.SelectedAssemblies.Count == 0)
            {
                Logger.Info("User cancelled the assemblies dialog.");
                return Result.Cancelled;
            }

            List<AssemblyInstance> selectedAssemblies = dialog.SelectedAssemblies;
            GraphicsStyle lineStyle = dialog.SelectedLineStyle;
            bool deleteMode = dialog.Mode == "Delete";

            PipeAxesPresetStorage.SaveSettings(document, dialog.Settings);

            string styleName = lineStyle.GraphicsStyleCategory?.Name ?? lineStyle.Name;
            Logger.Info(
                $"Selected assemblies: {selectedAssemblies.Count}. Line style: '{styleName}'. " +
                $"Mode: {(deleteMode ? "delete" : "apply")}.");

            PipeAxisResult result = new PipeAxisResult();
            Stopwatch stopwatch = Stopwatch.StartNew();

            List<PipeAxisService.AssemblyView> views = service.CollectViews(document, selectedAssemblies, result);

            if (views.Count == 0)
            {
                stopwatch.Stop();
                result.Elapsed = stopwatch.Elapsed;

                Logger.Warn("No plan or section views found for the selected assemblies. Nothing to do.");
                ShowReport(result, deleteMode, "Не найдено планов и разрезов выбранных сборок.");
                return Result.Succeeded;
            }

            using (TransactionGroup transactionGroup = new TransactionGroup(document, "Assembling Manager"))
            {
                transactionGroup.Start();
                Logger.Info("TransactionGroup started.");

                try
                {
                    string transactionName = deleteMode
                        ? "Удалить оси трубопроводов"
                        : "Оси трубопроводов";

                    using (Transaction transaction = new Transaction(document, transactionName))
                    {
                        FailureHandlingOptions failureOptions = transaction.GetFailureHandlingOptions();
                        failureOptions.SetFailuresPreprocessor(new FailurePreprocessor());
                        transaction.SetFailureHandlingOptions(failureOptions);

                        transaction.Start();
                        Logger.Info("Transaction started.");

                        if (deleteMode)
                        {
                            service.DeleteAxes(document, lineStyle, views, result);
                        }
                        else
                        {
                            service.ApplyAxes(document, lineStyle, views, dialog.Settings, result);
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

            Logger.Info(
                $"=== PipeAxisLinesCommand finished in {result.Elapsed.TotalSeconds:F2} s: " +
                $"views {result.ViewsProcessedCount}, created {result.LinesCreatedCount}, " +
                $"deleted {result.LinesDeletedCount}, pipes {result.PipesFoundCount}, " +
                $"skipped {result.PipesSkippedCount}, occluded {result.PipesFullyOccludedCount}, " +
                $"partial {result.PipesPartiallyVisibleCount}, rays {result.RaysCastCount}, " +
                $"hits {result.HitsReceivedCount}, warnings {result.Warnings.Count} ===");

            ShowReport(result, deleteMode, "Обработка завершена");

            return Result.Succeeded;
        }

        private static void ShowReport(PipeAxisResult result, bool deleteMode, string mainInstruction)
        {
            string content;

            if (deleteMode)
            {
                content = "Обработано видов: " + result.ViewsProcessedCount + "\n" +
                          "Удалено линий: " + result.LinesDeletedCount + "\n" +
                          "Предупреждений: " + result.Warnings.Count + " (подробности в логе плагина)\n" +
                          "Время работы: " + result.Elapsed.TotalSeconds.ToString("F1") + " с";
            }
            else
            {
                content = "Обработано видов: " + result.ViewsProcessedCount + "\n" +
                          "Создано линий: " + result.LinesCreatedCount + "\n" +
                          "Удалено старых линий: " + result.LinesDeletedCount + "\n" +
                          "Найдено труб: " + result.PipesFoundCount + "\n" +
                          "Пропущено труб: " + result.PipesSkippedCount + " (полностью перекрыто: " + result.PipesFullyOccludedCount + ")\n" +
                          "Частично перекрытых труб: " + result.PipesPartiallyVisibleCount + "\n" +
                          "Предупреждений: " + result.Warnings.Count + " (подробности в логе плагина)\n" +
                          "Время работы: " + result.Elapsed.TotalSeconds.ToString("F1") + " с";
            }

            TaskDialog report = new TaskDialog(Constants.PluginName)
            {
                MainInstruction = mainInstruction,
                MainContent = content,
                CommonButtons = TaskDialogCommonButtons.Ok
            };

            report.Show();
        }
    }
}
