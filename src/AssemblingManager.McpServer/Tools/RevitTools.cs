using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace AssemblingManager.McpServer.Tools;

[McpServerToolType]
public sealed class RevitTools
{
    private readonly BridgeDiscovery _discovery;
    private readonly BridgeClient _client;

    public RevitTools(BridgeDiscovery discovery, BridgeClient client)
    {
        _discovery = discovery;
        _client = client;
    }

    [McpServerTool]
    [Description(
        "Returns the connection status of the Assembling Manager Revit bridge: Revit version, " +
        "active document, active view, pipe size unit, and whether model changes are enabled. " +
        "Call this first. Use instancePid when more than one Revit is running.")]
    public Task<string> Status(
        int? instancePid = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            instancePid,
            "status",
            null,
            30000,
            cancellationToken);
    }

    [McpServerTool]
    [Description(
        "Lists running Revit MCP bridges (Revit instances). Use this when more than one Revit " +
        "is open, then pass the chosen pid as instancePid to other tools.")]
    public string ListInstances()
    {
        IReadOnlyList<BridgeDiscoveryFile> instances = _discovery.ReadAll();
        return JsonSerializer.Serialize(
            new
            {
                ok = true,
                bridgesDirectory = BridgeDiscovery.BridgesDirectory,
                instances = instances.Select(InstanceInfo).ToArray()
            },
            BridgeDiscovery.JsonOptions);
    }

    [McpServerTool]
    [Description(
        "Lists pipe segment catalogs (PipeSegment) in the active Revit document. " +
        "filter is a case-insensitive substring over name, material and schedule. " +
        "All diameters are in the document pipe size unit (see unitName). " +
        "Read-only.")]
    public Task<string> ListPipeSegments(
        string? filter = null,
        bool includeZeroUsage = true,
        int? instancePid = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            instancePid,
            "list_pipe_segments",
            new { filter, includeZeroUsage },
            60000,
            cancellationToken);
    }

    [McpServerTool]
    [Description(
        "Returns all sizes of one pipe segment catalog. Identify the segment by segmentId or " +
        "segmentName. All diameters are in the document pipe size unit (see unitName). " +
        "usedByPipes/usedPipeCount show whether a size is used by pipes in the project. " +
        "Read-only.")]
    public Task<string> GetPipeSegmentSizes(
        int? segmentId = null,
        string? segmentName = null,
        int? instancePid = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            instancePid,
            "get_pipe_segment_sizes",
            new { segmentId, segmentName },
            60000,
            cancellationToken);
    }

    [McpServerTool]
    [Description(
        "Builds a read-only preview of one pipe catalog size change and returns a single-use " +
        "previewId. operation is add, update or remove; nominalDiameter is the DN key and never " +
        "changes. For add pass outerDiameter and wallThickness. For update pass outerDiameter, " +
        "wallThickness, usedInSizeLists or usedInSizing. For remove pass only nominalDiameter. " +
        "All diameters are in the document pipe size unit (see unitName). A size used by pipes " +
        "cannot be removed. When the user asked to make the change, call apply_pipe_size_change " +
        "with the returned previewId in the same turn and report the applied result. The client " +
        "permission prompt is the confirmation; stop for a separate review only if the user " +
        "explicitly asked to see the plan first.")]
    public Task<string> PreviewPipeSizeChange(
        string operation,
        double nominalDiameter,
        int? segmentId = null,
        string? segmentName = null,
        double? outerDiameter = null,
        double? wallThickness = null,
        bool? usedInSizeLists = null,
        bool? usedInSizing = null,
        int? instancePid = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            instancePid,
            "preview_pipe_size_change",
            new
            {
                operation,
                segmentId,
                segmentName,
                nominalDiameter,
                outerDiameter,
                wallThickness,
                usedInSizeLists,
                usedInSizing
            },
            120000,
            cancellationToken);
    }

    [McpServerTool]
    [Description(
        "Applies exactly the pipe size change previously built by preview_pipe_size_change. " +
        "Requires the write switch to be enabled in the Assembling Manager plugin settings. " +
        "The previewId is single-use; an expired or stale preview fails with PREVIEW_EXPIRED or " +
        "CATALOG_CHANGED and must be previewed again. The whole change is one undo step (Ctrl+Z).")]
    public Task<string> ApplyPipeSizeChange(
        string previewId,
        int? instancePid = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            instancePid,
            "apply_pipe_size_change",
            new { previewId },
            300000,
            cancellationToken);
    }

    private async Task<string> ExecuteAsync(
        int? instancePid,
        string tool,
        object? args,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        try
        {
            BridgeDiscoveryFile instance = _discovery.Select(instancePid);
            JsonElement data = await _client.CallAsync(
                instance,
                tool,
                args,
                timeoutMs,
                cancellationToken);

            return JsonSerializer.Serialize(
                new
                {
                    ok = true,
                    instance = InstanceInfo(instance),
                    data
                },
                BridgeDiscovery.JsonOptions);
        }
        catch (BridgeNotFoundException ex)
        {
            return Error("BRIDGE_NOT_FOUND", ex.Message, ex.Instances);
        }
        catch (BridgeToolException ex)
        {
            return Error(ex.Code, ex.Message, null);
        }
        catch (OperationCanceledException)
        {
            return Error("CANCELLED", "Запрос отменён.", null);
        }
        catch (Exception ex)
        {
            return Error("MCP_SERVER_ERROR", ex.Message, null);
        }
    }

    private static string Error(
        string code,
        string message,
        IReadOnlyList<BridgeDiscoveryFile>? instances)
    {
        return JsonSerializer.Serialize(
            new
            {
                ok = false,
                error = new { code, message },
                instances = instances?.Select(InstanceInfo).ToArray()
            },
            BridgeDiscovery.JsonOptions);
    }

    private static object InstanceInfo(BridgeDiscoveryFile file)
    {
        return new
        {
            pid = file.Pid,
            revitVersion = file.RevitVersion,
            pluginVersion = file.PluginVersion,
            documentTitle = file.DocumentTitle,
            documentPath = file.DocumentPath,
            startedUtc = file.StartedUtc
        };
    }
}
