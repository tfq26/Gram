using Microsoft.Extensions.Configuration;

namespace Gram.Blazor.Shared;

public sealed class StorageService
{
    public string DataDirectory { get; }
    public string LogsDirectory { get; }

    public StorageService(IConfiguration? configuration = null)
    {
        // 1. Check the current variable and the legacy name used by older
        // deployments. Keeping both avoids silently switching data stores.
        var envDataDir = Environment.GetEnvironmentVariable("GRAM_DATA_DIR")
            ?? Environment.GetEnvironmentVariable("GRAM_STORAGE_DIR");
        
        // 2. Check configuration (appsettings.json / command line args)
        var configDataDir = configuration?["GRAM_DATA_DIR"] ?? configuration?["Storage:DataDir"];

        if (!string.IsNullOrWhiteSpace(envDataDir))
        {
            DataDirectory = envDataDir;
        }
        else if (!string.IsNullOrWhiteSpace(configDataDir))
        {
            DataDirectory = configDataDir;
        }
        else if (Directory.Exists("/app/data"))
        {
            // Standard Docker path
            DataDirectory = "/app/data";
        }
        else
        {
            // Standard Local Desktop AppData path
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            }
            DataDirectory = Path.Combine(appData, "Gram");
        }

        LogsDirectory = Path.Combine(DataDirectory, "logs");
        EnsureDirectories();
    }

    public void EnsureDirectories()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StorageService] Warning: Could not create directories: {ex.Message}");
        }
    }

    public string GetFilePath(string fileName) => Path.Combine(DataDirectory, fileName);
}
