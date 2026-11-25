using System.Diagnostics;
using System.Text.Json;

namespace AssemblingManager.McpServer;

public sealed class BridgeDiscovery
{
    public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string BridgesDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AssemblingManager",
            "Mcp",
            "bridges");

    public IReadOnlyList<BridgeDiscoveryFile> ReadAll()
    {
        if (!Directory.Exists(BridgesDirectory))
        {
            return Array.Empty<BridgeDiscoveryFile>();
        }

        List<BridgeDiscoveryFile> result = new List<BridgeDiscoveryFile>();

        foreach (string path in Directory.EnumerateFiles(BridgesDirectory, "bridge-*.json"))
        {
            try
            {
                string json = File.ReadAllText(path);
                BridgeDiscoveryFile? file = JsonSerializer.Deserialize<BridgeDiscoveryFile>(json, JsonOptions);
                if (file == null ||
                    file.Pid <= 0 ||
                    string.IsNullOrWhiteSpace(file.PipeName) ||
                    string.IsNullOrWhiteSpace(file.Token))
                {
                    continue;
                }

                if (!IsProcessAlive(file.Pid))
                {
                    continue;
                }

                result.Add(file);
            }
            catch (Exception)
            {
                // A bridge may be rewriting the discovery file; ignore and retry on the next call.
            }
        }

        return result
            .OrderByDescending(file => file.StartedUtc)
            .ToList();
    }

    public BridgeDiscoveryFile Select(int? instancePid)
    {
        IReadOnlyList<BridgeDiscoveryFile> instances = ReadAll();

        if (instancePid.HasValue)
        {
            BridgeDiscoveryFile? selected = instances.FirstOrDefault(file => file.Pid == instancePid.Value);
            if (selected != null)
            {
                return selected;
            }

            throw new BridgeNotFoundException(
                "Revit с pid " + instancePid.Value + " не найден среди запущенных MCP-мостов. " +
                "Проверь revit_list_instances.",
                instances);
        }

        if (instances.Count == 0)
        {
            throw new BridgeNotFoundException(
                "Revit не запущен или MCP-мост выключен. " +
                "Открой Revit 2025 и включи «Включить локальный мост» в Настройках плагина. " +
                "Каталог discovery: " + BridgesDirectory,
                instances);
        }

        if (instances.Count == 1)
        {
            return instances[0];
        }

        throw new BridgeNotFoundException(
            "Найдено несколько запущенных Revit. Укажи instancePid: " +
            Describe(instances) + ".",
            instances);
    }

    public static string Describe(IEnumerable<BridgeDiscoveryFile> instances)
    {
        return string.Join(
            "; ",
            instances.Select(file =>
                "pid " + file.Pid +
                " (Revit " + (string.IsNullOrWhiteSpace(file.RevitVersion) ? "?" : file.RevitVersion) +
                ", документ: " + (string.IsNullOrWhiteSpace(file.DocumentTitle) ? "<нет>" : file.DocumentTitle) + ")"));
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
