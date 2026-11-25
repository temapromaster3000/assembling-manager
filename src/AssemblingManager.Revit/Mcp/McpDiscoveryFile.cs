using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AssemblingManager.Revit.Services;

namespace AssemblingManager.Revit.Mcp
{
    internal sealed class McpDiscoveryFile
    {
        public McpDiscoveryFile()
        {
        }

        public int Protocol { get; set; } = McpBridgeService.ProtocolVersion;
        public int Pid { get; set; }
        public string PipeName { get; set; }
        public string Token { get; set; }
        public string PluginVersion { get; set; }
        public string RevitVersion { get; set; }
        public string UserName { get; set; }
        public string DocumentTitle { get; set; }
        public string DocumentPath { get; set; }
        public DateTime StartedUtc { get; set; }

        public static string BridgesDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AssemblingManager",
                    "Mcp",
                    "bridges");
            }
        }

        public string FilePath
        {
            get { return Path.Combine(BridgesDirectory, "bridge-" + Pid + ".json"); }
        }

        public void Write()
        {
            try
            {
                Directory.CreateDirectory(BridgesDirectory);
                string json = System.Text.Json.JsonSerializer.Serialize(this, McpJson.Options);
                string tempPath = FilePath + ".tmp";
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));

                if (File.Exists(FilePath))
                {
                    try
                    {
                        File.Replace(tempPath, FilePath, null);
                    }
                    catch (Exception)
                    {
                        File.Copy(tempPath, FilePath, true);
                        File.Delete(tempPath);
                    }
                }
                else
                {
                    File.Move(tempPath, FilePath);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[MCP] Could not write discovery file: " + ex.Message);
            }
        }

        public void Delete()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[MCP] Could not delete discovery file: " + ex.Message);
            }
        }

        public static string GenerateToken()
        {
            byte[] bytes = new byte[32];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes);
        }

        public static string GetPluginVersion()
        {
            try
            {
                return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        public static int GetCurrentProcessId()
        {
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    return process.Id;
                }
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
