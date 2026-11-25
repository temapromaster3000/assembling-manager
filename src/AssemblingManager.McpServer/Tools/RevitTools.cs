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
