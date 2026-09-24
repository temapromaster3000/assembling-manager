using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
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
    public class SortamentsCommand : IExternalCommand
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

            Logger.Info("=== SortamentsCommand started ===");
            Logger.Info($"Log file: {Logger.GetLogFilePath()}");
            Logger.Info($"Document: {document.Title}");
            Logger.Info($"Revit version: {uiApplication.Application.VersionNumber}");
            Assembly pluginAssembly = Assembly.GetExecutingAssembly();
            Logger.Info($"Assembly: {pluginAssembly.Location}");
            Logger.Info($"Assembly version: {pluginAssembly.GetName().Version}");

            try
            {
                SortamentService service = new SortamentService();
                ForgeTypeId unitId = service.GetPipeSizeUnitId(document);
                List<SortamentSegmentItem> segments = service.GetSegments(document);

                if (segments.Count == 0)
                {
                    TaskDialog.Show(Constants.PluginName, "В документе не найдено ни одного PipeSegment.");
                    return Result.Cancelled;
                }

                SortamentsDialog dialog = new SortamentsDialog(document, service, segments, unitId);
                if (dialog.ShowDialog() != true)
                {
                    return Result.Cancelled;
                }

                Logger.Info(
                    $"Queued edits={dialog.Edits?.Count ?? 0}, " +
                    $"unit={service.GetUnitName(unitId)}");
                if (dialog.Edits == null || dialog.Edits.Count == 0)
                {
                    TaskDialog.Show(Constants.PluginName, "Изменения не обнаружены.");
                    return Result.Cancelled;
                }

                foreach (SortamentEdit edit in dialog.Edits)
                {
                    Logger.Info(
                        $"Queued edit segmentId={edit.SegmentId}, rows={edit.Rows.Count}");
                }

                SortamentResult result = new SortamentResult
                {
                    OldSegmentDeleted = true
                };
                Stopwatch stopwatch = Stopwatch.StartNew();

                using (TransactionGroup transactionGroup = new TransactionGroup(document, "Assembling Manager"))
                {
                    TransactionStatus groupStartStatus = transactionGroup.Start();
                    Logger.Info("TransactionGroup start status=" + groupStartStatus);
                    if (groupStartStatus != TransactionStatus.Started)
                    {
                        throw new InvalidOperationException("Не удалось начать TransactionGroup.");
                    }

                    try
                    {
                        using (Transaction transaction = new Transaction(document, "Сортаменты труб"))
                        {
                            TransactionStatus transactionStartStatus = transaction.Start();
                            Logger.Info("Transaction start status=" + transactionStartStatus);
                            if (transactionStartStatus != TransactionStatus.Started)
                            {
                                throw new InvalidOperationException("Не удалось начать транзакцию.");
                            }

                            foreach (SortamentEdit edit in dialog.Edits)
                            {
                                SortamentResult segmentResult = new SortamentResult();
                                service.Apply(
                                    document,
                                    edit.SegmentId,
                                    edit.Rows,
                                    unitId,
                                    segmentResult);
                                MergeResults(result, segmentResult);
                            }

                            TransactionStatus transactionCommitStatus = transaction.Commit();
                            Logger.Info("Transaction commit status=" + transactionCommitStatus);
                            if (transactionCommitStatus != TransactionStatus.Committed)
                            {
                                throw new InvalidOperationException("Транзакция не была зафиксирована.");
                            }
                        }

                        TransactionStatus groupCommitStatus = transactionGroup.Assimilate();
                        Logger.Info("TransactionGroup assimilate status=" + groupCommitStatus);
                        if (groupCommitStatus != TransactionStatus.Committed)
                        {
                            throw new InvalidOperationException("TransactionGroup не был зафиксирован.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Sortaments failed: {ex}");
                        bool rollbackSucceeded = TryRollback(transactionGroup);

                        message = rollbackSucceeded
                            ? ex.Message
                            : ex.Message + " Откат не подтверждён; проверьте состояние модели вручную.";
                        TaskDialog.Show(Constants.PluginName, message);
                        return Result.Failed;
                    }
                }

                stopwatch.Stop();
                result.Elapsed = stopwatch.Elapsed;

                Logger.Info(
                    $"Sortaments completed: processedSegments={result.ProcessedSegmentCount}, " +
                    $"lastSegmentId={result.FinalSegmentId}, name='{result.FinalSegmentName}', " +
                    $"strategy={(result.UsedReplacement ? "replacement" : "direct")}, " +
                    $"added={result.AddedSizeCount}, changed={result.UpdatedSizeCount}, removed={result.RemovedSizeCount}, " +
                    $"pipes={result.MigratedPipeCount}, routingRules={result.UpdatedRoutingPreferenceCount}, " +
                    $"oldDeleted={result.OldSegmentDeleted}, warnings={result.Warnings.Count}");
                foreach (string detail in result.Details)
                {
                    Logger.Info("Sortaments detail: " + detail);
                }

                foreach (string warning in result.Warnings)
                {
                    Logger.Warn("Sortaments warning: " + warning);
                }

                ShowReport(result);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error($"Sortaments command failed: {ex}");
                message = ex.Message;
                TaskDialog.Show(Constants.PluginName, ex.Message);
                return Result.Failed;
            }
        }

        private static void MergeResults(SortamentResult target, SortamentResult source)
        {
            target.ProcessedSegmentCount++;
            target.AddedSizeCount += source.AddedSizeCount;
            target.UpdatedSizeCount += source.UpdatedSizeCount;
            target.RemovedSizeCount += source.RemovedSizeCount;
            target.MigratedPipeCount += source.MigratedPipeCount;
            target.UpdatedRoutingPreferenceCount += source.UpdatedRoutingPreferenceCount;
            target.UsedReplacement = target.UsedReplacement || source.UsedReplacement;
            target.OldSegmentDeleted = target.OldSegmentDeleted && source.OldSegmentDeleted;
            target.OriginalSegmentId = source.OriginalSegmentId;
            target.OriginalSegmentName = source.OriginalSegmentName;
            target.FinalSegmentId = source.FinalSegmentId;
            target.FinalSegmentName = source.FinalSegmentName;
            target.Details.Add(
                "Сегмент: " + source.OriginalSegmentName + " -> " + source.FinalSegmentName);
            target.Details.AddRange(source.Details);
            target.Warnings.AddRange(source.Warnings);
        }

        private static bool TryRollback(TransactionGroup transactionGroup)
        {
            if (!transactionGroup.HasStarted() || transactionGroup.HasEnded())
            {
                Logger.Error("Sortaments rollback was not available because the transaction group was already closed.");
                return false;
            }

            try
            {
                TransactionStatus status = transactionGroup.RollBack();
                if (status != TransactionStatus.RolledBack)
                {
                    Logger.Error("Sortaments rollback returned status " + status + ".");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Sortaments rollback failed: " + ex);
                return false;
            }
        }

        private static void ShowReport(SortamentResult result)
        {
            string content =
                "Изменено сегментов: " + result.ProcessedSegmentCount + "\n" +
                "Добавлено размеров: " + result.AddedSizeCount + "\n" +
                "Изменено размеров: " + result.UpdatedSizeCount + "\n" +
                "Удалено размеров: " + result.RemovedSizeCount;

            TaskDialog report = new TaskDialog(Constants.PluginName)
            {
                MainInstruction = "Сортаменты обработаны",
                MainContent = content,
                CommonButtons = TaskDialogCommonButtons.Ok
            };

            report.Show();
        }
    }
}
