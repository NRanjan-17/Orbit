namespace Orbit;

/// <summary>Well-known folders for Orbit.</summary>
public static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\Orbit — settings and logs.</summary>
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orbit");

    /// <summary>
    /// Capture folder. When running from a source checkout (an Orbit.slnx is found above the exe)
    /// this is &lt;repo&gt;\captures so captures sit next to the code; otherwise %LOCALAPPDATA%\Orbit\captures.
    /// </summary>
    public static string CapturesDirectory { get; } = FindRepoCaptures() ?? Path.Combine(DataDirectory, "captures");

    private static string? FindRepoCaptures()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Orbit.slnx")))
            {
                return Path.Combine(dir.FullName, "captures");
            }
        }

        return null;
    }
}
