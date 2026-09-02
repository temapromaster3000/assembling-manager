using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace AssemblingManager.Updater
{
    internal static class Program
    {
        private static readonly string[] CopyExtensions = { ".dll", ".pdb" };
        private const string RevitProcessName = "Revit";
        private const string UpdaterPrefix = "AssemblingManager.Updater";
        private static readonly TimeSpan RevitWaitTimeout = TimeSpan.FromMinutes(5);

        private static string _logPath;

        internal static int Main(string[] args)
        {
            string rootDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AssemblingManager");
            string markerPath = args != null && args.Length > 0
                ? args[0]
                : Path.Combine(rootDir, "update-pending.txt");
            _logPath = Path.Combine(rootDir, "updater-log.txt");

            Log("=== AssemblingManager.Updater started ===");

            try
            {
                if (!File.Exists(markerPath))
                {
                    Log("No pending update marker found. Exiting.");
                    return 0;
                }

                List<Artifact> artifacts = ParseMarker(File.ReadAllLines(markerPath));
                if (artifacts.Count == 0)
                {
                    Log("Marker contains no artifacts. Deleting marker.");
                    File.Delete(markerPath);
                    return 0;
                }

                Log("Pending update found: " + artifacts.Count + " artifact(s).");
                WaitForRevitExit();
                ApplyArtifacts(artifacts);
                CleanupStaging(rootDir, artifacts);

                File.Delete(markerPath);
                Log("Marker deleted. Update finished.");
                return 0;
            }
            catch (Exception ex)
            {
                Log("FATAL: " + ex);
                return 1;
            }
            finally
            {
                Log("=== AssemblingManager.Updater exited ===");
            }
        }

        private static List<Artifact> ParseMarker(string[] lines)
        {
            List<Artifact> artifacts = new List<Artifact>();
            Artifact current = null;

            foreach (string rawLine in lines)
            {
                string line = rawLine == null ? string.Empty : rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(line, "[Artifact]", StringComparison.OrdinalIgnoreCase))
                {
                    current = new Artifact();
                    artifacts.Add(current);
                    continue;
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0 || current == null)
                {
                    continue;
                }

                string key = line.Substring(0, separatorIndex).Trim();
                string value = line.Substring(separatorIndex + 1).Trim();
                if (string.Equals(key, "StagingDir", StringComparison.OrdinalIgnoreCase))
                {
                    current.StagingDir = value;
                }
                else if (string.Equals(key, "TargetDir", StringComparison.OrdinalIgnoreCase))
                {
                    current.TargetDir = value;
                }
            }

            return artifacts.Where(a =>
                !string.IsNullOrWhiteSpace(a.StagingDir) &&
                !string.IsNullOrWhiteSpace(a.TargetDir)).ToList();
        }

        private static void WaitForRevitExit()
        {
            int waitSeconds = 0;
            int maxWaitSeconds = (int)RevitWaitTimeout.TotalSeconds;

            while (waitSeconds < maxWaitSeconds)
            {
                Process[] revitProcesses = Process.GetProcessesByName(RevitProcessName);
                if (revitProcesses.Length == 0)
                {
                    Log("Revit has exited.");
                    return;
                }

                if (waitSeconds % 10 == 0)
                {
                    Log("Waiting for Revit to exit (" + revitProcesses.Length + " process(es))...");
                }

                foreach (Process process in revitProcesses)
                {
                    process.Dispose();
                }

                Thread.Sleep(1000);
                waitSeconds++;
            }

            Log("WARNING: timeout waiting for Revit to exit. Proceeding anyway.");
        }

        private static void ApplyArtifacts(List<Artifact> artifacts)
        {
            int copied = 0;
            int failed = 0;

            foreach (Artifact artifact in artifacts)
            {
                if (!Directory.Exists(artifact.StagingDir))
                {
                    Log("SKIP: staging directory not found: " + artifact.StagingDir);
                    continue;
                }

                if (!Directory.Exists(artifact.TargetDir))
                {
                    Directory.CreateDirectory(artifact.TargetDir);
                    Log("Created target directory: " + artifact.TargetDir);
                }

                Log("Applying: " + artifact.StagingDir + " -> " + artifact.TargetDir);

                foreach (string file in Directory.GetFiles(artifact.StagingDir))
                {
                    string fileName = Path.GetFileName(file);
                    string extension = Path.GetExtension(fileName).ToLowerInvariant();

                    if (!CopyExtensions.Contains(extension))
                    {
                        continue;
                    }

                    if (fileName.StartsWith(UpdaterPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        Log("SKIP: " + fileName + " (updater self-update is not allowed)");
                        continue;
                    }

                    try
                    {
                        File.Copy(file, Path.Combine(artifact.TargetDir, fileName), true);
                        copied++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Log("FAIL: " + fileName + " - " + ex.Message);
                    }
                }
            }

            Log("Files copied: " + copied + ", failed: " + failed + ".");
        }

        private static void CleanupStaging(string rootDir, List<Artifact> artifacts)
        {
            List<string> stagingDirs = artifacts
                .Select(a => a.StagingDir)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string defaultStagingDir = Path.Combine(rootDir, "staging");
            if (!stagingDirs.Contains(defaultStagingDir, StringComparer.OrdinalIgnoreCase))
            {
                stagingDirs.Add(defaultStagingDir);
            }

            foreach (string stagingDir in stagingDirs)
            {
                try
                {
                    if (Directory.Exists(stagingDir))
                    {
                        Directory.Delete(stagingDir, true);
                        Log("Staging directory deleted: " + stagingDir);
                    }
                }
                catch (Exception ex)
                {
                    Log("Could not delete staging directory: " + ex.Message);
                }
            }
        }

        private static void Log(string message)
        {
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message;
            Console.WriteLine(line);
            try
            {
                string logDir = Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrEmpty(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        private class Artifact
        {
            public string StagingDir { get; set; }
            public string TargetDir { get; set; }
        }
    }
}
