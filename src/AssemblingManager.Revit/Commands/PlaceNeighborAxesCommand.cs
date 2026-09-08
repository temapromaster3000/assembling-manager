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
    public class PlaceNeighborAxesCommand : IExternalCommand
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

            Logger.Info("=== PlaceNeighborAxesCommand started ===");
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

            NeighborAxesService service = new NeighborAxesService();
            List<string> worksetNames = service.GetWorksetNames(document);

            NeighborAxesSettings presetSettings = NeighborAxesPresetStorage.ReadSettings(document);

            SelectAssembliesDialog dialog = new SelectAssembliesDialog(assemblies, presetSettings, worksetNames);
            bool? dialogResult = dialog.ShowDialog();

            if (dialogResult != true || dialog.SelectedAssemblies == null || dialog.SelectedAssemblies.Count == 0)
            {
                Logger.Info("User cancelled the assemblies dialog.");
                return Result.Cancelled;
            }

            List<AssemblyInstance> selectedAssemblies = dialog.SelectedAssemblies;
            NeighborAxesSettings settings = dialog.Settings;

            NeighborAxesPresetStorage.SaveSettings(document, settings);

            Logger.Info(
                $"Selected assemblies: {selectedAssemblies.Count}. Radius: {settings.RadiusMm} mm. " +
                $"Grid workset: '{settings.GridWorksetName ?? "<all>"}'.");

            FamilySymbol symbol = service.FindMarkerSymbol(document);

            if (symbol == null)
            {
                Logger.Warn($"Family '{NeighborAxesService.MarkerFamilyName}' not found. Command cancelled.");
                TaskDialog.Show(
                    Constants.PluginName,
                    $"Семейство «{NeighborAxesService.MarkerFamilyName}» не найдено в проекте.\n\n" +
                    "Загрузите семейство в проект и повторите запуск.");
                return Result.Cancelled;
            }

            Logger.Info($"Marker family found: symbol '{symbol.Name}'.");

            NeighborAxesResult result = new NeighborAxesResult();
            Stopwatch stopwatch = Stopwatch.StartNew();

            WorksetId gridWorksetId = service.ResolveGridWorkset(document, settings.GridWorksetName);

            if (!string.IsNullOrWhiteSpace(settings.GridWorksetName) && gridWorksetId == null)
            {
                result.Warnings.Add(
                    $"Рабочий набор «{settings.GridWorksetName}» не найден — обрабатываются оси всех рабочих наборов.");
                Logger.Warn($"Workset '{settings.GridWorksetName}' not found. All worksets will be processed.");
            }

            List<NeighborAxesTask> tasks;

            try
            {
                tasks = service.BuildTasks(document, selectedAssemblies, settings, gridWorksetId, result);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to build neighbor axes tasks: {ex}");
                message = ex.Message;
                return Result.Failed;
            }

            Logger.Info($"Tasks built: {tasks.Count}.");

            if (tasks.Count == 0)
            {
                stopwatch.Stop();
                result.Elapsed = stopwatch.Elapsed;

                Logger.Warn("No views with a single visible axis found. Nothing to do.");
                ShowReport(result, "Не найдено видов, где видна только одна ось.");
                return Result.Succeeded;
            }

            using (TransactionGroup transactionGroup = new TransactionGroup(document, "Assembling Manager"))
            {
                transactionGroup.Start();
                Logger.Info("TransactionGroup started.");

                try
                {
                    using (Transaction transaction = new Transaction(document, "Ближайшие оси"))
                    {
                        FailureHandlingOptions failureOptions = transaction.GetFailureHandlingOptions();
                        failureOptions.SetFailuresPreprocessor(new FailurePreprocessor());
                        transaction.SetFailureHandlingOptions(failureOptions);

                        transaction.Start();
                        Logger.Info("Transaction started.");

                        service.ApplyTasks(document, symbol, tasks, result);

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
                $"=== PlaceNeighborAxesCommand finished in {result.Elapsed.TotalSeconds:F2} s: " +
                $"views {result.ViewsProcessedCount}, created {result.MarkersCreatedCount}, " +
                $"replaced {result.MarkersReplacedCount}, skipped {result.ViewsSkippedCount} ===");

            ShowReport(result, "Обработка завершена");

            return Result.Succeeded;
        }

        private static void ShowReport(NeighborAxesResult result, string mainInstruction)
        {
            TaskDialog report = new TaskDialog(Constants.PluginName)
            {
                MainInstruction = mainInstruction,
                MainContent = $"Создано маркеров: {result.MarkersCreatedCount}\n" +
                              $"Пропущено видов: {result.ViewsSkippedCount}\n" +
                              $"Предупреждений: {result.Warnings.Count} (подробности в логе плагина)\n" +
                              $"Время работы: {result.Elapsed.TotalSeconds:F1} с",
                CommonButtons = TaskDialogCommonButtons.Ok
            };

            report.Show();
        }
    }
}
