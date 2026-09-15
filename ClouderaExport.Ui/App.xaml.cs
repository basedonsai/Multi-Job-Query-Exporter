using System.IO;
using System.Windows;
using ClouderaExport.Core.Repositories;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace ClouderaExport.Ui;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        EnsureLoggingConfigured();
    }

    private static void EnsureLoggingConfigured()
    {
        try
        {
            if (LogManager.Configuration == null || LogManager.Configuration.AllTargets.Count == 0)
            {
                var config = new LoggingConfiguration();
                string logsDir = AppStateRepository.LogsDirectory;
                if (!Directory.Exists(logsDir))
                {
                    Directory.CreateDirectory(logsDir);
                }

                var fileTarget = new FileTarget("logfile")
                {
                    FileName = Path.Combine(logsDir, "app-${shortdate}.log"),
                    Layout = "${longdate}|${level:uppercase=true}|${logger}|${message} ${exception:format=tostring}",
                    ArchiveEvery = FileArchivePeriod.Day,
                    MaxArchiveFiles = 14,
                    KeepFileOpen = false,
                    Encoding = System.Text.Encoding.UTF8
                };

                config.AddRule(LogLevel.Info, LogLevel.Fatal, fileTarget);
                LogManager.Configuration = config;
            }
        }
        catch
        {
            // Logging configuration fail-safe
        }
    }
}
