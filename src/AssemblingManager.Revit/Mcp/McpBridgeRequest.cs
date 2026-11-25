using System.Text.Json;
using System.Threading;

namespace AssemblingManager.Revit.Mcp
{
    internal sealed class McpBridgeRequest
    {
        public string RequestId { get; set; }
        public string Tool { get; set; }
        public JsonElement Args { get; set; }
        public int TimeoutMs { get; set; }
        public ManualResetEventSlim Done { get; } = new ManualResetEventSlim(false);
        public string ResponseJson { get; set; }
        public volatile bool Abandoned;
    }
}
