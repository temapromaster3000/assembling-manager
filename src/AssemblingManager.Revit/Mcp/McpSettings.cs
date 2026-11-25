using System;
using System.IO;
using System.Text;
using System.Text.Json;
using AssemblingManager.Revit.Services;

namespace AssemblingManager.Revit.Mcp
{
    internal sealed class McpSettings
    {
        public McpSettings()
        {
        }

        public bool Enabled { get; set; }
        public bool AllowModelChanges { get; set; }

        public static string SettingsDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AssemblingManager");
            }
        }

        public static string SettingsFilePath
        {
            get { return Path.Combine(SettingsDirectory, "mcp-settings.json"); }
        }

        public static McpSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    return new McpSettings();
                }

                string json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
                McpSettings settings = JsonSerializer.Deserialize<McpSettings>(json, McpJson.Options);
                return settings ?? new McpSettings();
            }
            catch (Exception ex)
            {
                Logger.Warn("[MCP] Could not read MCP settings: " + ex.Message);
                return new McpSettings();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                string json = JsonSerializer.Serialize(this, McpJson.Options);
                File.WriteAllText(SettingsFilePath, json, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Logger.Warn("[MCP] Could not save MCP settings: " + ex.Message);
            }
        }
    }
}
