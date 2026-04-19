namespace Igres.Infrastructure.Storage;

internal static class AppDataPaths
{
    private static readonly string BaseDirectory = InitializeBaseDirectory();

    public static string UserDataDirectory => BaseDirectory;
    public static string SessionFile => Path.Combine(BaseDirectory, "session.bin");
    public static string CapturedHeadersFile => Path.Combine(BaseDirectory, "captured.bin");
    public static string PreferencesFile => Path.Combine(BaseDirectory, "preferences.json");

    private static string InitializeBaseDirectory()
    {
        var exeDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(exeDirectory))
        {
            exeDirectory = Environment.CurrentDirectory;
        }

        var dataDirectory = Path.Combine(exeDirectory, "user-data");
        Directory.CreateDirectory(dataDirectory);
        TryDeleteLegacyDumpFiles(dataDirectory);
        TryDeleteLegacyRoamingData();
        return dataDirectory;
    }

    private static void TryDeleteLegacyDumpFiles(string dataDirectory)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(dataDirectory, "dump-*.txt"))
            {
                File.Delete(file);
            }
        }
        catch
        {
            // Best-effort local cleanup only.
        }
    }

    private static void TryDeleteLegacyRoamingData()
    {
        try
        {
            var roamingRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(roamingRoot))
            {
                roamingRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config");
            }

            var legacyFolder = Path.Combine(roamingRoot, "Igres");
            if (!Directory.Exists(legacyFolder))
            {
                legacyFolder = Path.Combine(roamingRoot, "IGRes");
                if (!Directory.Exists(legacyFolder))
                {
                    return;
                }
            }

            foreach (var fileName in new[] { "session.bin", "captured.bin", "preferences.json" })
            {
                var path = Path.Combine(legacyFolder, fileName);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            if (!Directory.EnumerateFileSystemEntries(legacyFolder).Any())
            {
                Directory.Delete(legacyFolder);
            }
        }
        catch
        {
            // Best-effort migration cleanup only.
        }
    }
}
