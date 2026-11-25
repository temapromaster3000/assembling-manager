using System;
using System.Collections.Generic;
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

        private static SortamentSegmentItem ResolveSegment(
            List<SortamentSegmentItem> segments,
            McpGetPipeSegmentSizesArgs args)
        {
            if (args.SegmentId.HasValue)
            {
                SortamentSegmentItem byId = segments.FirstOrDefault(item => item.SegmentId == args.SegmentId.Value);
                if (byId == null)
                {
                    throw new McpToolException(
                        McpErrorCodes.InvalidArgument,
                        "Сегмент с id " + args.SegmentId.Value + " не найден.");
                }

                return byId;
            }

            string name = args.SegmentName?.Trim();
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
