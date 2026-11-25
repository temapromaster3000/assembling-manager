using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AssemblingManager.Core.Models;
using AssemblingManager.Revit.Services;

namespace AssemblingManager.Revit.Mcp
{
    internal sealed class McpToolDispatcher
    {
        private readonly McpBridgeService _bridge;
        private readonly McpPreviewStore _previews = new McpPreviewStore();

        public McpToolDispatcher(McpBridgeService bridge)
        {
            _bridge = bridge;
        }

        public object Dispatch(UIApplication app, McpBridgeRequest request)
        {
            switch (request.Tool)
            {
                case "status":
                    return BuildStatus(app);
                case "list_pipe_segments":
                    return ListPipeSegments(app, request.Args);
                case "get_pipe_segment_sizes":
                    return GetPipeSegmentSizes(app, request.Args);
                case "preview_pipe_size_change":
                    return PreviewPipeSizeChange(app, request.Args);
                case "apply_pipe_size_change":
                    return ApplyPipeSizeChange(app, request.Args);
                default:
                    throw new McpToolException(
                        McpErrorCodes.UnsupportedTool,
                        "Инструмент '" + request.Tool + "' не поддерживается этой версией плагина.");
            }
        }

        private McpStatusResult BuildStatus(UIApplication app)
        {
            Application application = app.Application;
            Document document = app.ActiveUIDocument?.Document;
            McpStatusResult status = new McpStatusResult
            {
                BridgeVersion = "0.1.0",
                PluginVersion = McpDiscoveryFile.GetPluginVersion(),
                RevitVersion = application.VersionNumber,
                RevitVersionName = application.VersionName,
                UserName = application.Username,
                WriteEnabled = _bridge.Settings.AllowModelChanges,
                LogPath = Logger.GetLogFilePath(),
                PipeName = _bridge.PipeName,
                StartedUtc = _bridge.StartedUtc
            };

            if (document != null)
            {
                _bridge.RecordDocumentInfo(document.Title, document.PathName);

                status.Document = new McpDocumentInfo
                {
                    Title = document.Title,
                    PathName = document.PathName,
                    IsWorkshared = document.IsWorkshared,
                    IsReadOnly = document.IsReadOnly,
                    IsModified = document.IsModified
                };

                View activeView = app.ActiveUIDocument?.ActiveView;
                if (activeView != null)
                {
                    status.ActiveView = new McpViewInfo
                    {
                        Id = GetElementIdValue(activeView.Id),
                        Name = activeView.Name,
                        ViewType = activeView.ViewType.ToString()
                    };
                }

                try
                {
                    SortamentService service = new SortamentService();
                    string unitName = service.GetUnitName(service.GetPipeSizeUnitId(document));
                    status.PipeSizeUnit = string.IsNullOrWhiteSpace(unitName) ? null : unitName;
                }
                catch (Exception ex)
                {
                    Logger.Debug("[MCP] Could not read pipe size unit: " + ex.Message);
                }
            }
            else
            {
                _bridge.RecordDocumentInfo(null, null);
            }

            return status;
        }

        private McpListSegmentsResult ListPipeSegments(UIApplication app, JsonElement argsElement)
        {
            Document document = RequireDocument(app);
            McpListPipeSegmentsArgs args = ParseArgs<McpListPipeSegmentsArgs>(argsElement);

            SortamentService service = new SortamentService();
            List<SortamentSegmentItem> segments = service.GetSegments(document);
            bool includeZeroUsage = args.IncludeZeroUsage ?? true;
            string filter = args.Filter?.Trim();

            IEnumerable<SortamentSegmentItem> query = segments;
            if (!includeZeroUsage)
            {
                query = query.Where(item => item.UsedPipeCount > 0);
            }

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(item =>
                    Contains(item.Name, filter) ||
                    Contains(item.MaterialName, filter) ||
                    Contains(item.ScheduleName, filter));
            }

            McpListSegmentsResult result = new McpListSegmentsResult
            {
                UnitName = GetUnitName(service, document)
            };

            foreach (SortamentSegmentItem item in query)
            {
                result.Segments.Add(ToSegmentInfo(item));
            }

            return result;
        }

        private McpSegmentSizesResult GetPipeSegmentSizes(UIApplication app, JsonElement argsElement)
        {
            Document document = RequireDocument(app);
            McpGetPipeSegmentSizesArgs args = ParseArgs<McpGetPipeSegmentSizesArgs>(argsElement);

            SortamentService service = new SortamentService();
            List<SortamentSegmentItem> segments = service.GetSegments(document);
            SortamentSegmentItem segment = ResolveSegment(segments, args);

            ForgeTypeId unitId = service.GetPipeSizeUnitId(document);
            string unitName = service.GetUnitName(unitId);
            List<SortamentSizeRow> rows = service.GetSizeRows(document, segment.SegmentId, unitId);

            McpSegmentSizesResult result = new McpSegmentSizesResult
            {
                Segment = ToSegmentInfo(segment),
                UnitName = string.IsNullOrWhiteSpace(unitName) ? null : unitName
            };

            foreach (SortamentSizeRow row in rows)
            {
                result.Sizes.Add(new McpSegmentSizeInfo
                {
                    NominalDiameter = Round(row.NominalDiameter),
                    OuterDiameter = Round(row.OuterDiameter),
                    WallThickness = Round(row.WallThickness),
                    InnerDiameter = Round(row.InnerDiameter),
                    UsedInSizeLists = row.UsedInSizeLists,
                    UsedInSizing = row.UsedInSizing,
                    UsedByPipes = row.IsUsedByPipes,
                    UsedPipeCount = row.UsedPipeCount
                });
            }

            return result;
        }

        private McpPreviewChangeResult PreviewPipeSizeChange(UIApplication app, JsonElement argsElement)
        {
            Document document = RequireDocument(app);
            if (document.IsReadOnly)
            {
                throw new McpToolException(
                    McpErrorCodes.ReadOnlyDocument,
                    "Документ открыт только для чтения.");
            }

            McpPreviewPipeSizeChangeArgs args = ParseArgs<McpPreviewPipeSizeChangeArgs>(argsElement);
            string operation = NormalizeOperation(args.Operation);
            double nominalDiameter = RequirePositive(args.NominalDiameter, "nominalDiameter");

            SortamentService service = new SortamentService();
            List<SortamentSegmentItem> segments = service.GetSegments(document);
            SortamentSegmentItem segment = ResolveSegment(segments, args.SegmentId, args.SegmentName);

            ForgeTypeId unitId = service.GetPipeSizeUnitId(document);
            string unitName = service.GetUnitName(unitId);
            List<SortamentSizeRow> currentRows = service.GetSizeRows(document, segment.SegmentId, unitId);
            List<SortamentSizeRow> proposedRows = BuildProposedRows(operation, args, currentRows, unitName);

            SortamentChangePlan plan;
            try
            {
                plan = service.PreviewChange(document, segment.SegmentId, proposedRows, unitId);
            }
            catch (InvalidOperationException ex)
            {
                throw new McpToolException(McpErrorCodes.InvalidArgument, ex.Message);
            }

            bool writeEnabled = _bridge.Settings.AllowModelChanges;
            bool canApply = plan.HasChanges && plan.Blockers.Count == 0 && !document.IsReadOnly && writeEnabled;

            McpPreviewChangeResult result = new McpPreviewChangeResult
            {
                CanApply = canApply,
                WriteEnabled = writeEnabled,
                Operation = operation,
                Segment = ToSegmentInfo(segment),
                UnitName = string.IsNullOrWhiteSpace(unitName) ? null : unitName,
                NominalDiameter = Round(nominalDiameter),
                HasChanges = plan.HasChanges,
                UsedPipeCount = plan.UsedPipeCount,
                RoutingPreferenceRuleCount = plan.RoutingPreferenceRuleCount,
                AddedSizeCount = plan.AddedSizeCount,
                UpdatedSizeCount = plan.UpdatedSizeCount,
                RemovedSizeCount = plan.RemovedSizeCount,
                CatalogHash = plan.CatalogHash
            };

            foreach (SortamentSizeChange change in plan.Changes)
            {
                result.Changes.Add(new McpSizeChangeInfo
                {
                    Operation = change.Operation,
                    NominalDiameter = Round(change.NominalDiameter),
                    OuterDiameter = Round(change.OuterDiameter),
                    WallThickness = Round(change.WallThickness),
                    InnerDiameter = Round(change.InnerDiameter),
                    UsedInSizeLists = change.UsedInSizeLists,
                    UsedInSizing = change.UsedInSizing,
                    UsedByPipes = change.UsedByPipes,
                    UsedPipeCount = change.UsedPipeCount,
                    PreviousOuterDiameter = change.PreviousOuterDiameter.HasValue
                        ? Round(change.PreviousOuterDiameter.Value)
                        : (double?)null,
                    PreviousWallThickness = change.PreviousWallThickness.HasValue
                        ? Round(change.PreviousWallThickness.Value)
                        : (double?)null,
                    PreviousUsedInSizeLists = change.PreviousUsedInSizeLists,
                    PreviousUsedInSizing = change.PreviousUsedInSizing
                });
            }

            foreach (SortamentSizeChange size in plan.ProposedSizes)
            {
                result.ProposedSizes.Add(ToSizeInfo(size));
            }

            foreach (SortamentWorksharingBlocker blocker in plan.Blockers)
            {
                result.Blockers.Add(new McpWorksharingBlockerInfo
                {
                    ElementId = blocker.ElementId,
                    Kind = blocker.Kind,
                    Name = blocker.Name,
                    Owner = blocker.Owner,
                    Reason = blocker.Reason
                });
            }

            result.Warnings.AddRange(plan.Warnings);

            if (plan.HasChanges && plan.Blockers.Count == 0)
            {
                McpPreviewEntry entry = _previews.Create(
                    new DocumentIdentity(document.PathName, document.Title),
                    segment.SegmentId,
                    segment.Name,
                    plan.CatalogHash,
                    unitId.TypeId,
                    operation,
                    nominalDiameter,
                    proposedRows);

                result.PreviewId = entry.PreviewId;
                result.ExpiresUtc = entry.ExpiresUtc;
            }

            if (!plan.HasChanges)
            {
                result.Message = "Изменений не обнаружено: каталог уже совпадает с запросом.";
            }
            else if (plan.Blockers.Count > 0)
            {
                result.Message = "Применение невозможно: элементы каталога заняты или устарели. " +
                                 "Освободи их и сделай предпросмотр заново.";
            }
            else if (!writeEnabled)
            {
                result.Message = "План готов, но запись выключена. Включи «Разрешить изменения модели» " +
                                 "в настройках плагина и вызови apply_pipe_size_change с previewId.";
            }
            else
            {
                result.Message = "План готов. Примени его через apply_pipe_size_change с previewId.";
            }

            return result;
        }

        private McpApplyChangeResult ApplyPipeSizeChange(UIApplication app, JsonElement argsElement)
        {
            if (!_bridge.Settings.AllowModelChanges)
            {
                throw new McpToolException(
                    McpErrorCodes.WriteDisabled,
                    "Запись выключена. Включи «Разрешить изменения модели» в настройках плагина.");
            }

            Document document = RequireDocument(app);
            if (document.IsReadOnly)
            {
                throw new McpToolException(
                    McpErrorCodes.ReadOnlyDocument,
                    "Документ открыт только для чтения.");
            }

            McpApplyPipeSizeChangeArgs args = ParseArgs<McpApplyPipeSizeChangeArgs>(argsElement);
            string previewId = args.PreviewId?.Trim();
            if (string.IsNullOrEmpty(previewId))
            {
                throw new McpToolException(
                    McpErrorCodes.InvalidArgument,
                    "Укажи previewId, полученный от preview_pipe_size_change.");
            }

            if (!_previews.TryTake(previewId, out McpPreviewEntry entry))
            {
                throw new McpToolException(
                    McpErrorCodes.PreviewExpired,
                    "Предпросмотр не найден или уже использован. Сделай preview_pipe_size_change заново.");
            }

            if (entry.ExpiresUtc <= DateTime.UtcNow)
            {
                throw new McpToolException(
                    McpErrorCodes.PreviewExpired,
                    "Предпросмотр устарел. Сделай preview_pipe_size_change заново.");
            }

            if (!string.Equals(entry.DocumentPath, document.PathName ?? string.Empty, StringComparison.Ordinal) ||
                !string.Equals(entry.DocumentTitle, document.Title ?? string.Empty, StringComparison.Ordinal))
            {
                throw new McpToolException(
                    McpErrorCodes.PreviewExpired,
                    "Предпросмотр сделан для другого документа. Сделай его заново в активном документе.");
            }

            SortamentService service = new SortamentService();
            ForgeTypeId unitId;
            try
            {
                unitId = service.GetPipeSizeUnitId(document);
            }
            catch (Exception ex)
            {
                throw new McpToolException(
                    McpErrorCodes.CatalogChanged,
                    "Не удалось прочитать единицы труб: " + ex.Message);
            }

            if (!string.Equals(unitId.TypeId, entry.UnitTypeId, StringComparison.Ordinal))
            {
                throw new McpToolException(
                    McpErrorCodes.PreviewExpired,
                    "Единицы измерения труб изменились после предпросмотра. Сделай его заново.");
            }

            string currentHash;
            try
            {
                currentHash = service.GetCatalogHash(document, entry.SegmentId);
            }
            catch (InvalidOperationException ex)
            {
                throw new McpToolException(
                    McpErrorCodes.CatalogChanged,
                    "Каталог изменился после предпросмотра: " + ex.Message);
            }

            if (!string.Equals(currentHash, entry.CatalogHash, StringComparison.Ordinal))
            {
                throw new McpToolException(
                    McpErrorCodes.CatalogChanged,
                    "Каталог или сам PipeSegment изменились после предпросмотра. Сделай предпросмотр заново.");
            }

            List<ElementId> affectedIds;
            try
            {
                affectedIds = service.GetAffectedElementIds(document, entry.SegmentId);
            }
            catch (InvalidOperationException ex)
            {
                throw new McpToolException(
                    McpErrorCodes.CatalogChanged,
                    "Каталог изменился после предпросмотра: " + ex.Message);
            }

            WorksharingEditability editability = new WorksharingService().CheckEditable(document, affectedIds);
            if (editability.SkippedElements.Count > 0)
            {
                string list = string.Join(
                    "; ",
                    editability.SkippedElements
                        .Take(5)
                        .Select(item => "id " + GetElementIdValue(item.Id) + " — " + item.Reason +
                                        (string.IsNullOrWhiteSpace(item.Owner) ? string.Empty : " (" + item.Owner + ")")));
                throw new McpToolException(
                    McpErrorCodes.WorksharingBusy,
                    "Некоторые элементы заняты, изменение отменено: " + list +
                    (editability.SkippedElements.Count > 5
                        ? "; и ещё " + (editability.SkippedElements.Count - 5) + "."
                        : "."));
            }

            SortamentChangePlan plan;
            try
            {
                plan = service.PreviewChange(document, entry.SegmentId, entry.Rows, unitId);
            }
            catch (InvalidOperationException ex)
            {
                throw new McpToolException(
                    McpErrorCodes.CatalogChanged,
                    "Каталог или использование труб изменились после предпросмотра: " + ex.Message);
            }

            McpApplyChangeResult result = new McpApplyChangeResult
            {
                PreviewId = entry.PreviewId,
                AddedSizeCount = plan.AddedSizeCount,
                UpdatedSizeCount = plan.UpdatedSizeCount,
                RemovedSizeCount = plan.RemovedSizeCount
            };

            if (!plan.HasChanges)
            {
                result.NoChanges = true;
                result.FinalSegmentId = plan.SegmentId;
                result.FinalSegmentName = plan.SegmentName;
                result.Message = "Изменений не обнаружено: каталог уже совпадает с планом.";
                return result;
            }

            SortamentResult applyResult = new SortamentResult();
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using (TransactionGroup transactionGroup = new TransactionGroup(document, "Assembling Manager: MCP сортаменты"))
            {
                TransactionStatus groupStartStatus = transactionGroup.Start();
                if (groupStartStatus != TransactionStatus.Started)
                {
                    throw new McpToolException(
                        McpErrorCodes.RevitApiError,
                        "Не удалось начать TransactionGroup.");
                }

                try
                {
                    using (Transaction transaction = new Transaction(document, "Сортаменты труб (MCP)"))
                    {
                        TransactionStatus transactionStartStatus = transaction.Start();
                        if (transactionStartStatus != TransactionStatus.Started)
                        {
                            throw new InvalidOperationException("Не удалось начать транзакцию.");
                        }

                        service.Apply(document, entry.SegmentId, entry.Rows, unitId, applyResult);

                        TransactionStatus transactionCommitStatus = transaction.Commit();
                        if (transactionCommitStatus != TransactionStatus.Committed)
                        {
                            throw new InvalidOperationException("Транзакция не была зафиксирована.");
                        }
                    }

                    TransactionStatus groupCommitStatus = transactionGroup.Assimilate();
                    if (groupCommitStatus != TransactionStatus.Committed)
                    {
                        throw new InvalidOperationException("TransactionGroup не был зафиксирован.");
                    }
                }
                catch (Exception ex)
                {
                    bool rolledBack = TryRollback(transactionGroup);
                    Logger.Error("[MCP] apply_pipe_size_change failed: " + ex);
                    throw new McpToolException(
                        McpErrorCodes.RevitApiError,
                        ex.Message + (rolledBack
                            ? " Изменения отменены."
                            : " Откат не подтверждён; проверьте состояние модели вручную."));
                }
            }

            stopwatch.Stop();
            applyResult.Elapsed = stopwatch.Elapsed;

            result.Segment = new McpSegmentInfo
            {
                Id = entry.SegmentId,
                Name = entry.SegmentName
            };
            result.FinalSegmentId = applyResult.FinalSegmentId;
            result.FinalSegmentName = applyResult.FinalSegmentName;
            result.AddedSizeCount = applyResult.AddedSizeCount;
            result.UpdatedSizeCount = applyResult.UpdatedSizeCount;
            result.RemovedSizeCount = applyResult.RemovedSizeCount;
            result.MigratedPipeCount = applyResult.MigratedPipeCount;
            result.UpdatedRoutingPreferenceCount = applyResult.UpdatedRoutingPreferenceCount;
            result.UsedReplacement = applyResult.UsedReplacement;
            result.OldSegmentDeleted = applyResult.OldSegmentDeleted;
            result.ElapsedMs = stopwatch.ElapsedMilliseconds;
            result.Details.AddRange(applyResult.Details);
            result.Warnings.AddRange(applyResult.Warnings);
            result.Message = "Изменения применены. Отменить можно одним Ctrl+Z.";
            return result;
        }

        private static List<SortamentSizeRow> BuildProposedRows(
            string operation,
            McpPreviewPipeSizeChangeArgs args,
            List<SortamentSizeRow> currentRows,
            string unitName)
        {
            double nominalDiameter = args.NominalDiameter.Value;
            SortamentSizeRow existing = FindRow(currentRows, nominalDiameter);
            string unitSuffix = string.IsNullOrWhiteSpace(unitName) ? string.Empty : " " + unitName;

            switch (operation)
            {
                case "add":
                {
                    if (existing != null)
                    {
                        throw new McpToolException(
                            McpErrorCodes.InvalidArgument,
                            "Размер DN " + FormatValue(nominalDiameter) + unitSuffix +
                            " уже есть в каталоге. Для изменения используй operation=update.");
                    }

                    double outerDiameter = RequirePositive(args.OuterDiameter, "outerDiameter");
                    double wallThickness = RequirePositive(args.WallThickness, "wallThickness");
                    List<SortamentSizeRow> rows = currentRows.Select(row => row.Clone()).ToList();
                    rows.Add(new SortamentSizeRow
                    {
                        IsExisting = false,
                        NominalDiameter = nominalDiameter,
                        OuterDiameter = outerDiameter,
                        WallThickness = wallThickness,
                        UsedInSizeLists = args.UsedInSizeLists ?? true,
                        UsedInSizing = args.UsedInSizing ?? true
                    });
                    return rows;
                }

                case "update":
                {
                    if (existing == null)
                    {
                        throw new McpToolException(
                            McpErrorCodes.InvalidArgument,
                            "Размер DN " + FormatValue(nominalDiameter) + unitSuffix +
                            " не найден в каталоге. Для добавления используй operation=add.");
                    }

                    if (!args.OuterDiameter.HasValue &&
                        !args.WallThickness.HasValue &&
                        !args.UsedInSizeLists.HasValue &&
                        !args.UsedInSizing.HasValue)
                    {
                        throw new McpToolException(
                            McpErrorCodes.InvalidArgument,
                            "Для update укажи outerDiameter, wallThickness, usedInSizeLists или usedInSizing.");
                    }

                    SortamentSizeRow updated = existing.Clone();
                    if (args.OuterDiameter.HasValue)
                    {
                        updated.OuterDiameter = RequirePositive(args.OuterDiameter, "outerDiameter");
                    }

                    if (args.WallThickness.HasValue)
                    {
                        updated.WallThickness = RequirePositive(args.WallThickness, "wallThickness");
                    }

                    if (args.UsedInSizeLists.HasValue)
                    {
                        updated.UsedInSizeLists = args.UsedInSizeLists.Value;
                    }

                    if (args.UsedInSizing.HasValue)
                    {
                        updated.UsedInSizing = args.UsedInSizing.Value;
                    }

                    List<SortamentSizeRow> rows = currentRows.Select(row => row.Clone()).ToList();
                    int index = rows.FindIndex(row => IsNearlyEqual(row.NominalDiameter, nominalDiameter));
                    rows[index] = updated;
                    return rows;
                }

                case "remove":
                {
                    if (existing == null)
                    {
                        throw new McpToolException(
                            McpErrorCodes.InvalidArgument,
                            "Размер DN " + FormatValue(nominalDiameter) + unitSuffix +
                            " не найден в каталоге.");
                    }

                    if (existing.IsUsedByPipes)
                    {
                        throw new McpToolException(
                            McpErrorCodes.InvalidArgument,
                            "Нельзя удалить DN " + FormatValue(nominalDiameter) + unitSuffix +
                            ": он используется " + existing.UsedPipeCount + " труб(ами) в проекте.");
                    }

                    return currentRows
                        .Where(row => !IsNearlyEqual(row.NominalDiameter, nominalDiameter))
                        .Select(row => row.Clone())
                        .ToList();
                }

                default:
                    throw new McpToolException(
                        McpErrorCodes.InvalidArgument,
                        "operation должна быть add, update или remove.");
            }
        }

        private static string NormalizeOperation(string operation)
        {
            string normalized = operation?.Trim().ToLowerInvariant();
            if (normalized == "add" || normalized == "update" || normalized == "remove")
            {
                return normalized;
            }

            throw new McpToolException(
                McpErrorCodes.InvalidArgument,
                "operation должна быть add, update или remove.");
        }

        private static double RequirePositive(double? value, string name)
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value <= 0.0)
            {
                throw new McpToolException(
                    McpErrorCodes.InvalidArgument,
                    name + " должен быть положительным числом.");
            }

            return value.Value;
        }

        private static SortamentSizeRow FindRow(List<SortamentSizeRow> rows, double nominalDiameter)
        {
            return rows.FirstOrDefault(row => IsNearlyEqual(row.NominalDiameter, nominalDiameter));
        }

        private static bool IsNearlyEqual(double first, double second)
        {
            return Math.Abs(first - second) <= 0.000001;
        }

        private static string FormatValue(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static McpSegmentSizeInfo ToSizeInfo(SortamentSizeChange size)
        {
            return new McpSegmentSizeInfo
            {
                NominalDiameter = Round(size.NominalDiameter),
                OuterDiameter = Round(size.OuterDiameter),
                WallThickness = Round(size.WallThickness),
                InnerDiameter = Round(size.InnerDiameter),
                UsedInSizeLists = size.UsedInSizeLists,
                UsedInSizing = size.UsedInSizing,
                UsedByPipes = size.UsedByPipes,
                UsedPipeCount = size.UsedPipeCount
            };
        }

        private static bool TryRollback(TransactionGroup transactionGroup)
        {
            try
            {
                if (!transactionGroup.HasStarted() || transactionGroup.HasEnded())
                {
                    return false;
                }

                return transactionGroup.RollBack() == TransactionStatus.RolledBack;
            }
            catch (Exception ex)
            {
                Logger.Error("[MCP] Sortament transaction rollback failed: " + ex);
                return false;
            }
        }

        private static SortamentSegmentItem ResolveSegment(
            List<SortamentSegmentItem> segments,
            McpGetPipeSegmentSizesArgs args)
        {
            return ResolveSegment(segments, args.SegmentId, args.SegmentName);
        }

        private static SortamentSegmentItem ResolveSegment(
            List<SortamentSegmentItem> segments,
            int? segmentId,
            string segmentName)
        {
            if (segmentId.HasValue)
            {
                SortamentSegmentItem byId = segments.FirstOrDefault(item => item.SegmentId == segmentId.Value);
                if (byId == null)
                {
                    throw new McpToolException(
                        McpErrorCodes.InvalidArgument,
                        "Сегмент с id " + segmentId.Value + " не найден.");
                }

                return byId;
            }

            string name = segmentName?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                throw new McpToolException(
                    McpErrorCodes.InvalidArgument,
                    "Укажи segmentId или segmentName.");
            }

            List<SortamentSegmentItem> exact = segments
                .Where(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (exact.Count == 1)
            {
                return exact[0];
            }

            if (exact.Count > 1)
            {
                throw AmbiguousSegment(name, exact);
            }

            List<SortamentSegmentItem> partial = segments
                .Where(item => !string.IsNullOrEmpty(item.Name) &&
                               item.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            if (partial.Count == 1)
            {
                return partial[0];
            }

            if (partial.Count == 0)
            {
                throw new McpToolException(
                    McpErrorCodes.InvalidArgument,
                    "Сегмент '" + name + "' не найден.");
            }

            throw AmbiguousSegment(name, partial);
        }

        private static McpToolException AmbiguousSegment(string name, List<SortamentSegmentItem> candidates)
        {
            string list = string.Join(", ", candidates.Select(item => item.Name + " (id " + item.SegmentId + ")"));
            return new McpToolException(
                McpErrorCodes.InvalidArgument,
                "По имени '" + name + "' найдено несколько сегментов: " + list + ". Укажи segmentId.");
        }

        private static McpSegmentInfo ToSegmentInfo(SortamentSegmentItem item)
        {
            return new McpSegmentInfo
            {
                Id = item.SegmentId,
                Name = item.Name,
                Material = item.MaterialName,
                Schedule = item.ScheduleName,
                UsedPipeCount = item.UsedPipeCount
            };
        }

        private static string GetUnitName(SortamentService service, Document document)
        {
            try
            {
                string unitName = service.GetUnitName(service.GetPipeSizeUnitId(document));
                return string.IsNullOrWhiteSpace(unitName) ? null : unitName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool Contains(string value, string filter)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static double Round(double value)
        {
            return Math.Round(value, 3, MidpointRounding.AwayFromZero);
        }

        private static T ParseArgs<T>(JsonElement element) where T : new()
        {
            if (element.ValueKind == JsonValueKind.Undefined || element.ValueKind == JsonValueKind.Null)
            {
                return new T();
            }

            try
            {
                T value = JsonSerializer.Deserialize<T>(element.GetRawText(), McpJson.Options);
                return value ?? new T();
            }
            catch (JsonException ex)
            {
                throw new McpToolException(McpErrorCodes.InvalidArgument, "Некорректные аргументы: " + ex.Message);
            }
        }

        private static Document RequireDocument(UIApplication app)
        {
            Document document = app.ActiveUIDocument?.Document;
            if (document == null)
            {
                throw new McpToolException(
                    McpErrorCodes.NoDocument,
                    "В Revit нет открытого документа.");
            }

            return document;
        }

        private static int GetElementIdValue(ElementId id)
        {
#pragma warning disable CS0618
            return id.IntegerValue;
#pragma warning restore CS0618
        }
    }
}
