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
}
