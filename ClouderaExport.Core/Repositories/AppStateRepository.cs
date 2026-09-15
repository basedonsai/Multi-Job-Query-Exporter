using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClouderaExport.Core.Models;
using NLog;

namespace ClouderaExport.Core.Repositories;

public class AppStateRepository
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly object _fileLock = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private string? _resolvedFilePath;

    public AppStateRepository()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public static string ConfigDirectory
    {
        get
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Cloudera Export");
        }
    }

    public static string LogsDirectory => Path.Combine(ConfigDirectory, "Logs");

    public static string DefaultExportDirectory
    {
        get
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(docs, "Cloudera Export", "Exports");
        }
    }

    public string ActiveStoragePath
    {
        get
        {
            if (string.IsNullOrEmpty(_resolvedFilePath))
            {
                _resolvedFilePath = ResolveStoragePath();
            }
            return _resolvedFilePath;
        }
    }

    public string DisplayStoragePath => ActiveStoragePath;

    private string ResolveStoragePath()
    {
        try
        {
            string configDir = ConfigDirectory;
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            string primaryConfigPath = Path.Combine(configDir, "config.json");
            Logger.Info("Using primary LocalAppData storage location: {0}", primaryConfigPath);
            return primaryConfigPath;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to initialize LocalAppData folder. Falling back to application directory.");
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(appDir, "config.json");
        }
    }

    public AppState Load()
    {
        lock (_fileLock)
        {
            string filePath = ActiveStoragePath;

            // Backward compatibility: If config.json doesn't exist, search for legacy jobs.json / config.json locations
            if (!File.Exists(filePath))
            {
                MigrateLegacyConfig(filePath);
            }

            if (!File.Exists(filePath))
            {
                Logger.Info("No existing configuration found at {0}. Initializing fresh AppState.", filePath);
                return new AppState();
            }

            try
            {
                string json = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new AppState();
                }

                AppState? state = JsonSerializer.Deserialize<AppState>(json, _jsonOptions);
                Logger.Info("Successfully loaded configuration from {0} ({1} jobs, {2} connections).",
                    filePath, state?.Jobs?.Count ?? 0, state?.Connections?.Count ?? 0);

                return state ?? new AppState();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to load/parse configuration from {0}. Returning empty AppState.", filePath);
                return new AppState();
            }
        }
    }

    private void MigrateLegacyConfig(string targetConfigPath)
    {
        try
        {
            // Possible legacy paths in order of preference:
            string[] legacyCandidates =
            [
                // 1. Same directory, old filename: %LocalAppData%\Cloudera Export\jobs.json
                Path.Combine(ConfigDirectory, "jobs.json"),
                // 2. Previous AppData location: %AppData%\ClouderaExport\jobs.json
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClouderaExport", "jobs.json"),
                // 3. Previous AppData location: %AppData%\Cloudera Export\config.json
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cloudera Export", "config.json"),
                // 4. Application base directory: jobs.json
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "jobs.json"),
                // 5. Application base directory: config.json
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json")
            ];

            foreach (var candidate in legacyCandidates)
            {
                if (File.Exists(candidate) && !string.Equals(candidate, targetConfigPath, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info("Migrating existing configuration from legacy path '{0}' to '{1}'", candidate, targetConfigPath);
                    string targetDir = Path.GetDirectoryName(targetConfigPath)!;
                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    File.Copy(candidate, targetConfigPath, overwrite: true);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed during legacy config migration probe.");
        }
    }

    public void Save(AppState state)
    {
        lock (_fileLock)
        {
            string filePath = ActiveStoragePath;
            string dir = Path.GetDirectoryName(filePath)!;

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Rotate backups if jobs.json already exists: .bak5 <- .bak4 <- .bak3 <- .bak2 <- .bak1 <- jobs.json
            if (File.Exists(filePath))
            {
                RotateBackups(filePath);
            }

            string json = JsonSerializer.Serialize(state, _jsonOptions);

            // Write atomically via temporary file
            string tempFile = Path.Combine(dir, $".config_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempFile, json);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            File.Move(tempFile, filePath);

            Logger.Info("Successfully saved configuration to {0}", filePath);
        }
    }

    private void RotateBackups(string primaryFilePath)
    {
        try
        {
            const int maxBackups = 5;

            // Shift older backups backwards (bak4 -> bak5, bak3 -> bak4, etc.)
            for (int i = maxBackups - 1; i >= 1; i--)
            {
                string source = $"{primaryFilePath}.bak{i}";
                string target = $"{primaryFilePath}.bak{i + 1}";

                if (File.Exists(source))
                {
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                    File.Move(source, target);
                }
            }

            // Move current primary to .bak1
            string bak1 = $"{primaryFilePath}.bak1";
            if (File.Exists(bak1))
            {
                File.Delete(bak1);
            }
            File.Copy(primaryFilePath, bak1, overwrite: true);
            Logger.Debug("Rotated backups for {0}", primaryFilePath);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Warning: Failed to rotate backups for {0}", primaryFilePath);
        }
    }
}
