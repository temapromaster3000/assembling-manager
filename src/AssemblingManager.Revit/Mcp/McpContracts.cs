using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AssemblingManager.Revit.Mcp
{
    internal sealed class McpRequest
    {
        public McpRequest()
        {
        }

        public int Protocol { get; set; }
        public string Token { get; set; }
        public string RequestId { get; set; }
        public string Tool { get; set; }
        public JsonElement Args { get; set; }
        public int TimeoutMs { get; set; }
    }

    internal sealed class McpResponse
    {
        public int Protocol { get; set; } = McpBridgeService.ProtocolVersion;
        public string RequestId { get; set; }
        public bool Ok { get; set; }
        public JsonElement? Result { get; set; }
        public McpError Error { get; set; }
        public long ElapsedMs { get; set; }
    }

    internal sealed class McpError
    {
        public McpError()
        {
        }

        public McpError(string code, string message)
        {
            Code = code;
            Message = message;
        }

        public string Code { get; set; }
        public string Message { get; set; }
    }

    internal sealed class McpStatusResult
    {
        public bool Connected { get; set; } = true;
        public int Protocol { get; set; } = McpBridgeService.ProtocolVersion;
        public string BridgeVersion { get; set; }
        public string PluginVersion { get; set; }
        public string RevitVersion { get; set; }
        public string RevitVersionName { get; set; }
        public string UserName { get; set; }
        public McpDocumentInfo Document { get; set; }
        public McpViewInfo ActiveView { get; set; }
        public string PipeSizeUnit { get; set; }
        public bool WriteEnabled { get; set; }
        public string LogPath { get; set; }
        public string PipeName { get; set; }
        public DateTime StartedUtc { get; set; }
    }

    internal sealed class McpDocumentInfo
    {
        public string Title { get; set; }
        public string PathName { get; set; }
        public bool IsWorkshared { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsModified { get; set; }
    }

    internal sealed class McpViewInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ViewType { get; set; }
    }

    internal sealed class McpSegmentInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Material { get; set; }
        public string Schedule { get; set; }
        public int UsedPipeCount { get; set; }
    }

    internal sealed class McpSegmentSizeInfo
    {
        public double NominalDiameter { get; set; }
        public double OuterDiameter { get; set; }
        public double WallThickness { get; set; }
        public double InnerDiameter { get; set; }
        public bool UsedInSizeLists { get; set; }
        public bool UsedInSizing { get; set; }
        public bool UsedByPipes { get; set; }
        public int UsedPipeCount { get; set; }
    }

    internal sealed class McpListSegmentsResult
    {
        public string UnitName { get; set; }
        public List<McpSegmentInfo> Segments { get; set; } = new List<McpSegmentInfo>();
    }

    internal sealed class McpSegmentSizesResult
    {
        public McpSegmentInfo Segment { get; set; }
        public string UnitName { get; set; }
        public List<McpSegmentSizeInfo> Sizes { get; set; } = new List<McpSegmentSizeInfo>();
    }

    internal sealed class McpListPipeSegmentsArgs
    {
        public McpListPipeSegmentsArgs()
        {
        }

        public string Filter { get; set; }
        public bool? IncludeZeroUsage { get; set; }
    }

    internal sealed class McpGetPipeSegmentSizesArgs
    {
        public McpGetPipeSegmentSizesArgs()
        {
        }

        public int? SegmentId { get; set; }
        public string SegmentName { get; set; }
    }

    internal sealed class McpPreviewPipeSizeChangeArgs
    {
        public McpPreviewPipeSizeChangeArgs()
        {
        }

        public string Operation { get; set; }
        public int? SegmentId { get; set; }
        public string SegmentName { get; set; }
        public double? NominalDiameter { get; set; }
        public double? OuterDiameter { get; set; }
        public double? WallThickness { get; set; }
        public bool? UsedInSizeLists { get; set; }
        public bool? UsedInSizing { get; set; }
    }

    internal sealed class McpApplyPipeSizeChangeArgs
    {
        public McpApplyPipeSizeChangeArgs()
        {
        }

        public string PreviewId { get; set; }
    }

    internal sealed class McpPreviewChangeResult
    {
        public string PreviewId { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public bool CanApply { get; set; }
        public bool WriteEnabled { get; set; }
        public string Operation { get; set; }
        public McpSegmentInfo Segment { get; set; }
        public string UnitName { get; set; }
        public double NominalDiameter { get; set; }
        public bool HasChanges { get; set; }
        public int UsedPipeCount { get; set; }
        public int RoutingPreferenceRuleCount { get; set; }
        public int AddedSizeCount { get; set; }
        public int UpdatedSizeCount { get; set; }
        public int RemovedSizeCount { get; set; }
        public string CatalogHash { get; set; }
        public List<McpSizeChangeInfo> Changes { get; set; } = new List<McpSizeChangeInfo>();
        public List<McpSegmentSizeInfo> ProposedSizes { get; set; } = new List<McpSegmentSizeInfo>();
        public List<McpWorksharingBlockerInfo> Blockers { get; set; } = new List<McpWorksharingBlockerInfo>();
        public List<string> Warnings { get; set; } = new List<string>();
        public string Message { get; set; }
    }

    internal sealed class McpSizeChangeInfo
    {
        public string Operation { get; set; }
        public double NominalDiameter { get; set; }
        public double OuterDiameter { get; set; }
        public double WallThickness { get; set; }
        public double InnerDiameter { get; set; }
        public bool UsedInSizeLists { get; set; }
        public bool UsedInSizing { get; set; }
        public bool UsedByPipes { get; set; }
        public int UsedPipeCount { get; set; }
        public double? PreviousOuterDiameter { get; set; }
        public double? PreviousWallThickness { get; set; }
        public bool? PreviousUsedInSizeLists { get; set; }
        public bool? PreviousUsedInSizing { get; set; }
    }

    internal sealed class McpWorksharingBlockerInfo
    {
        public int ElementId { get; set; }
        public string Kind { get; set; }
        public string Name { get; set; }
        public string Owner { get; set; }
        public string Reason { get; set; }
    }

    internal sealed class McpApplyChangeResult
    {
        public string PreviewId { get; set; }
        public bool NoChanges { get; set; }
        public McpSegmentInfo Segment { get; set; }
        public int FinalSegmentId { get; set; }
        public string FinalSegmentName { get; set; }
        public int AddedSizeCount { get; set; }
        public int UpdatedSizeCount { get; set; }
        public int RemovedSizeCount { get; set; }
        public int MigratedPipeCount { get; set; }
        public int UpdatedRoutingPreferenceCount { get; set; }
        public bool UsedReplacement { get; set; }
        public bool OldSegmentDeleted { get; set; }
        public long ElapsedMs { get; set; }
        public List<string> Details { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public string Message { get; set; }
    }
}
