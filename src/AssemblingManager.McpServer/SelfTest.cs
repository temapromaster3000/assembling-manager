using System.Text.Json;

namespace AssemblingManager.McpServer;

public static class SelfTest
{
    public static async Task<int> RunAsync()
    {
        BridgeDiscovery discovery = new BridgeDiscovery();
        BridgeClient client = new BridgeClient();
        IReadOnlyList<BridgeDiscoveryFile> instances = discovery.ReadAll();

        var report = new
        {
            ok = true,
            bridgesDirectory = BridgeDiscovery.BridgesDirectory,
            instances = instances.Select(file => new
            {
                pid = file.Pid,
                revitVersion = file.RevitVersion,
                pluginVersion = file.PluginVersion,
                documentTitle = file.DocumentTitle,
                documentPath = file.DocumentPath,
                startedUtc = file.StartedUtc,
                status = (object?)null,
                error = (string?)null
            }).ToList()
        };

        bool hasFailure = false;

        for (int i = 0; i < instances.Count; i++)
        {
            BridgeDiscoveryFile instance = instances[i];
            try
            {
                JsonElement status = await client.GetStatusAsync(instance, CancellationToken.None);
                report.instances[i] = new
                {
                    pid = instance.Pid,
                    revitVersion = instance.RevitVersion,
                    pluginVersion = instance.PluginVersion,
                    documentTitle = instance.DocumentTitle,
                    documentPath = instance.DocumentPath,
                    startedUtc = instance.StartedUtc,
                    status = (object?)status,
                    error = (string?)null
                };
            }
            catch (Exception ex)
            {
                hasFailure = true;
                report.instances[i] = new
                {
                    pid = instance.Pid,
                    revitVersion = instance.RevitVersion,
                    pluginVersion = instance.PluginVersion,
                    documentTitle = instance.DocumentTitle,
                    documentPath = instance.DocumentPath,
                    startedUtc = instance.StartedUtc,
                    status = (object?)null,
                    error = (string?)ex.Message
                };
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                ok = !hasFailure,
                report.bridgesDirectory,
                report.instances
            },
            BridgeDiscovery.JsonOptions));

        return hasFailure ? 1 : 0;
    }
}
