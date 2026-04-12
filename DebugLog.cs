using System;
using System.IO;
using Godot;

namespace LoadOrderManager;

internal static class DebugLog
{
    private static readonly object Sync = new();
    private static string? _logPath;

    public static string LogPath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_logPath))
            {
                return _logPath;
            }

            try
            {
                var userDataDir = OS.GetUserDataDir();
                var dir = Path.Combine(userDataDir, "LoadOrderManager");
                Directory.CreateDirectory(dir);
                _logPath = Path.Combine(dir, "load_order_manager.log");
            }
            catch
            {
                _logPath = "load_order_manager.log";
            }

            return _logPath;
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var line = $"[LoadOrderManager] [{timestamp}] [{level}] {message}";
        var full = ex == null ? line : $"{line}{System.Environment.NewLine}{ex}";

        try
        {
            if (level == "ERROR")
            {
                GD.PrintErr(line);
            }
            else
            {
                GD.Print(line);
            }
        }
        catch
        {
            // ignored
        }

        try
        {
            lock (Sync)
            {
                File.AppendAllText(LogPath, full + System.Environment.NewLine);
            }
        }
        catch
        {
            // ignored
        }
    }
}
