using AssemblingManager.McpServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

internal static class Program
{
    private const string ServerInstructions =
        "You are connected to Autodesk Revit through the Assembling Manager plugin. " +
        "Call the status tool first. Use list_instances when more than one Revit is running; " +
        "pass the chosen pid as instancePid to other tools. " +
        "Use list_pipe_segments and get_pipe_segment_sizes to inspect pipe catalogs. " +
        "All diameters are in the document pipe size unit; read unitName before interpreting numbers. " +
        "Do not invent segment ids, diameters or wall thicknesses. Ask the user when unsure. " +
        "To change a catalog, call preview_pipe_size_change first, then always call " +
        "apply_pipe_size_change with the returned previewId in the same turn when the user asked " +
        "to make the change. The preview is still mandatory and performs the safety checks; do not " +
        "apply a preview built for a different request. Do not stop for a separate confirmation " +
        "unless the user explicitly asks to review the plan first: the client permission prompt is " +
        "the confirmation. Report the applied result. Writing also requires the write switch in the " +
        "plugin settings; status reports writeEnabled.";

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                return await SelfTest.RunAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("selftest failed: " + ex);
                return 1;
            }
        }

        try
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            builder.Services.AddSingleton<BridgeDiscovery>();
            builder.Services.AddSingleton<BridgeClient>();
            builder.Services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new()
                    {
                        Name = "revit",
                        Version = "0.1.0"
                    };
                    options.ServerInstructions = ServerInstructions;
                })
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            await builder.Build().RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("MCP server failed: " + ex);
            return 1;
        }
    }
}
