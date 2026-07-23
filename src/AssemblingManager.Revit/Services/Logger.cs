using System;
using System.Diagnostics;
using System.IO;

namespace AssemblingManager.Revit.Services
{
    public static class Logger
    {
        private static readonly string LogFilePath;
        private static readonly object LockObject = new object();

        static Logger()
        {
            string logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk",
                "Revit",
                "Addins",
                "AssemblingManager",
                "Logs");

            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            LogFilePath = Path.Combine(logDirectory, $"assembling-manager-{timestamp}.log");
        }

        public static string GetLogFilePath()
        {
            return LogFilePath;
        }

        public static void Info(string message)
        {
            Write("INF", message);
        }

        public static void Debug(string message)
        {
            Write("DBG", message);
        }

        public static void Warn(string message)
        {
            Write("WRN", message);
        }

        public static void Error(string message)
        {
            Write("ERR", message);
        }

        public static void Time(string operation, Action action)
        {
            Time(operation, () => { action(); return true; });
        }

        public static T Time<T>(string operation, Func<T> func)
        {
            Stopwatch sw = Stopwatch.StartNew();
            Info($"START {operation}");
            try
            {
                T result = func();
                Info($"END {operation}: {sw.Elapsed.TotalSeconds:F3} s");
                return result;
            }
            catch (Exception ex)
            {
                Error($"FAILED {operation}: {ex.Message}");
                throw;
            }
        }

        private static void Write(string level, string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            lock (LockObject)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
    }
}
