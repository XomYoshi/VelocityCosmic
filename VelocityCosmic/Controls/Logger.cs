using System;
using System.IO;

#nullable enable
namespace VelocityCosmic.Controls;

internal static class Logger
{
    private static readonly string LogDir = Path.Combine(Environment.GetFolderPath((Environment.SpecialFolder)26), "Velocity Ui");
    private static readonly string LogFile = Path.Combine(Logger.LogDir, "tab_session_log.txt");

    public static void Log(string message)
    {
        try
        {
            if (!Directory.Exists(Logger.LogDir))
                Directory.CreateDirectory(Logger.LogDir);
            string str = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}{Environment.NewLine}";
            File.AppendAllText(Logger.LogFile, str);
        }
        catch
        {
        }
    }
}

