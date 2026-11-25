using System.Text.Json;

namespace AssemblingManager.McpServer;

public sealed class BridgeDiscoveryFile
{
    public int Protocol { get; set; }
    public int Pid { get; set; }
    public string PipeName { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string PluginVersion { get; set; } = string.Empty;
    public string RevitVersion { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? DocumentTitle { get; set; }
    public string? DocumentPath { get; set; }
    public DateTime StartedUtc { get; set; }
}

public sealed class BridgeRequest
{
    public int Protocol { get; set; } = 1;
    public string Token { get; set; } = string.Empty;
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string Tool { get; set; } = string.Empty;
    public object? Args { get; set; }
    public int TimeoutMs { get; set; }
}

public sealed class BridgeResponse
{
    public int Protocol { get; set; }
    public string? RequestId { get; set; }
    public bool Ok { get; set; }
    public JsonElement Result { get; set; }
    public BridgeError? Error { get; set; }
    public long ElapsedMs { get; set; }
}

public sealed class BridgeError
{
    public string? Code { get; set; }
    public string? Message { get; set; }
}

public sealed class BridgeToolException : Exception
{
    public BridgeToolException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class BridgeNotFoundException : Exception
{
    public BridgeNotFoundException(string message, IReadOnlyList<BridgeDiscoveryFile> instances)
        : base(message)
    {
        Instances = instances;
    }

    public IReadOnlyList<BridgeDiscoveryFile> Instances { get; }
}
