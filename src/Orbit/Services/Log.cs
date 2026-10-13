using System.Globalization;

namespace Orbit.Services;

/// <summary>Tiny append-only diagnostics log at %LOCALAPPDATA%\Orbit\orbit.log (no device addresses are written).</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(AppPaths.DataDirectory, "orbit.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1_000_000)
                {
                    File.Delete(FilePath);
                }

                File.AppendAllText(FilePath, string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}"));
            }
        }
        catch (IOException)
        {
        }
    }

    public static void Error(string where, Exception ex) => Write($"ERROR {where}: {ex}");
}
