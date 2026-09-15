using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using ClouderaExport.Core.Models;
using ClouderaExport.Core.Repositories;
using ClouderaExport.Core.Services;
using NLog;

namespace ClouderaExport.Ui.ViewModels;

public class MainViewModel : ViewModelBase
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly AppStateRepository _repository;
    private readonly ExcelExportService _exportService;
    private readonly DispatcherTimer _autoSaveTimer;
    private bool _isLoading;
    private bool _isRunning;

    private JobItemViewModel? _selectedJob;
    private ConnectionItemViewModel? _selectedConnection;
    private string _autoSaveStatus = "Ready";
    private string _logText = string.Empty;

    public MainViewModel()
    {
        _repository = new AppStateRepository();
        _exportService = new ExcelExportService();

        Jobs = new ObservableCollection<JobItemViewModel>();
        Connections = new ObservableCollection<ConnectionItemViewModel>();

        // Wire collection changes to trigger autosave
        Jobs.CollectionChanged += (s, e) => TriggerChange();
        Connections.CollectionChanged += (s, e) => TriggerChange();

        // 1-second debounce timer
        _autoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _autoSaveTimer.Tick += (s, e) =>
        {
            _autoSaveTimer.Stop();
            SaveNow();
        };

        // Commands
        RunSelectedCommand = new RelayCommand(async () => await RunSelectedJobAsync(), () => !IsRunning && SelectedJob != null);
        RunAllCommand = new RelayCommand(async () => await RunAllJobsAsync(), () => !IsRunning && Jobs.Any(j => j.Enabled));

        AddJobCommand = new RelayCommand(AddJob, () => !IsRunning);
        DeleteJobCommand = new RelayCommand(DeleteJob, () => !IsRunning && SelectedJob != null);
        DuplicateJobCommand = new RelayCommand(DuplicateJob, () => !IsRunning && SelectedJob != null);
        BrowseOutputFolderCommand = new RelayCommand(BrowseOutputFolder, () => !IsRunning && SelectedJob != null);

        AddConnectionCommand = new RelayCommand(AddConnection, () => !IsRunning);
        DeleteConnectionCommand = new RelayCommand(DeleteConnection, () => !IsRunning && SelectedConnection != null);
        TestConnectionCommand = new RelayCommand(TestConnection, () => !IsRunning && SelectedConnection != null);

        OpenConfigFolderCommand = new RelayCommand(() => OpenFolder(AppStateRepository.ConfigDirectory, "Config Folder"));
        OpenLogsFolderCommand = new RelayCommand(() => OpenFolder(AppStateRepository.LogsDirectory, "Logs Folder"));
        OpenExportFolderCommand = new RelayCommand(() => OpenFolder(!string.IsNullOrWhiteSpace(SelectedJob?.OutputFolder) ? SelectedJob.OutputFolder : DefaultExportFolder, "Export Folder"));
        BrowseDefaultExportFolderCommand = new RelayCommand(BrowseDefaultExportFolder, () => !IsRunning);

        // Initial Load
        LoadInitialState();
    }

    public ObservableCollection<JobItemViewModel> Jobs { get; }
    public ObservableCollection<ConnectionItemViewModel> Connections { get; }

    public Array AvailableFormats => Enum.GetValues(typeof(ExportFormat));
    public Array AvailableConnectionTypes => Enum.GetValues(typeof(ConnectionType));

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetField(ref _isRunning, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public JobItemViewModel? SelectedJob
    {
        get => _selectedJob;
        set
        {
            if (SetField(ref _selectedJob, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public ConnectionItemViewModel? SelectedConnection
    {
        get => _selectedConnection;
        set
        {
            if (SetField(ref _selectedConnection, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private GridLength _logPanelHeight = new GridLength(140);

    public GridLength LogPanelHeight
    {
        get => _logPanelHeight;
        set => SetField(ref _logPanelHeight, value);
    }

    public string AutoSaveStatus
    {
        get => _autoSaveStatus;
        set => SetField(ref _autoSaveStatus, value);
    }

    public string LogText
    {
        get => _logText;
        set => SetField(ref _logText, value);
    }

    private string _defaultExportFolder = string.Empty;

    public string DefaultExportFolder
    {
        get => _defaultExportFolder;
        set
        {
            if (SetField(ref _defaultExportFolder, value))
            {
                TriggerChange();
            }
        }
    }

    public ICommand RunSelectedCommand { get; }
    public ICommand RunAllCommand { get; }

    public ICommand AddJobCommand { get; }
    public ICommand DeleteJobCommand { get; }
    public ICommand DuplicateJobCommand { get; }
    public ICommand BrowseOutputFolderCommand { get; }

    public ICommand AddConnectionCommand { get; }
    public ICommand DeleteConnectionCommand { get; }
    public ICommand TestConnectionCommand { get; }

    public ICommand OpenConfigFolderCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }
    public ICommand OpenExportFolderCommand { get; }
    public ICommand BrowseDefaultExportFolderCommand { get; }

    public void TriggerChange()
    {
        if (_isLoading || _isRunning) return;

        AutoSaveStatus = "Saving changes...";
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    public void UpdateLogPanelHeight(double pixels)
    {
        if (pixels < 50) pixels = 50;
        LogPanelHeight = new GridLength(pixels);
        TriggerChange();
    }

    public void SaveNow()
    {
        try
        {
            var state = new AppState
            {
                Settings = new UiSettings
                {
                    LogPanelHeight = LogPanelHeight.Value >= 40 ? LogPanelHeight.Value : 140.0,
                    DefaultExportFolder = DefaultExportFolder
                },
                Connections = Connections.Select(c => c.ToModel()).ToList(),
                Jobs = Jobs.Select(j => j.ToModel()).ToList()
            };

            _repository.Save(state);
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            AutoSaveStatus = $"Auto-saved at {timestamp} ({_repository.DisplayStoragePath})";
            AppendLog($"Auto-saved configuration to {_repository.DisplayStoragePath}");
        }
        catch (Exception ex)
        {
            AutoSaveStatus = "Auto-save failed!";
            AppendLog($"ERROR during auto-save: {ex.Message}");
            Logger.Error(ex, "Failed to auto-save configuration");
        }
    }

    public void FlushSave()
    {
        if (_autoSaveTimer.IsEnabled)
        {
            _autoSaveTimer.Stop();
            SaveNow();
        }
    }

    private void LoadInitialState()
    {
        _isLoading = true;
        try
        {
            AppendLog($"Initializing configuration from repository (Storage: {_repository.DisplayStoragePath})...");
            var state = _repository.Load();

            if (state.Settings != null && state.Settings.LogPanelHeight >= 50)
            {
                LogPanelHeight = new GridLength(state.Settings.LogPanelHeight);
            }

            if (state.Settings != null && !string.IsNullOrWhiteSpace(state.Settings.DefaultExportFolder))
            {
                DefaultExportFolder = state.Settings.DefaultExportFolder;
            }
            else
            {
                DefaultExportFolder = AppStateRepository.DefaultExportDirectory;
            }

            try
            {
                if (!Directory.Exists(DefaultExportFolder))
                {
                    Directory.CreateDirectory(DefaultExportFolder);
                }
            }
            catch { }

            Connections.Clear();
            foreach (var conn in state.Connections)
            {
                Connections.Add(new ConnectionItemViewModel(conn, TriggerChange));
            }

            Jobs.Clear();
            foreach (var job in state.Jobs)
            {
                Jobs.Add(new JobItemViewModel(job, TriggerChange));
            }

            SelectedJob = Jobs.FirstOrDefault();
            SelectedConnection = Connections.FirstOrDefault();

            AutoSaveStatus = $"Loaded ({_repository.DisplayStoragePath})";
            AppendLog($"Configuration loaded: {Connections.Count} connection(s), {Jobs.Count} job(s).");
        }
        catch (Exception ex)
        {
            AppendLog($"Error loading configuration: {ex.Message}");
            Logger.Error(ex, "Failed to load initial configuration");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task RunSelectedJobAsync()
    {
        if (SelectedJob == null) return;

        FlushSave();
        IsRunning = true;
        AutoSaveStatus = $"Running job '{SelectedJob.Name}'...";

        AppendLog($"========== Starting Execution: '{SelectedJob.Name}' ==========");

        try
        {
            var jobModel = SelectedJob.ToModel();
            var connModel = Connections.FirstOrDefault(c => c.Id == jobModel.ConnectionId)?.ToModel();

            var result = await Task.Run(() => _exportService.ExecuteJobAsync(
                jobModel,
                connModel!,
                msg => System.Windows.Application.Current.Dispatcher.Invoke(() => AppendLog(msg))));

            AutoSaveStatus = $"Job '{SelectedJob.Name}' completed ({result.TotalRows:N0} rows).";
            AppendLog($"========== Finished: '{SelectedJob.Name}' ({result.GeneratedFiles.Count} parts, {result.TotalRows:N0} rows) ==========");

            MessageBox.Show(
                $"Export completed successfully!\n\n" +
                $"Job: {jobModel.Name}\n" +
                $"Total Rows: {result.TotalRows:N0}\n" +
                $"Files Created: {result.GeneratedFiles.Count}\n" +
                $"Duration: {result.Elapsed.TotalSeconds:F2} seconds\n\n" +
                $"Output Folder:\n{jobModel.OutputFolder}",
                "Job Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AutoSaveStatus = $"Job '{SelectedJob.Name}' failed!";
            AppendLog($"ERROR: Full exception for '{SelectedJob.Name}':\n{ex}");
            Logger.Error(ex, "Execution failed for job '{0}'", SelectedJob.Name);

            MessageBox.Show(
                $"Failed to export job '{SelectedJob.Name}':\n\n{ex.Message}",
                "Job Execution Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task RunAllJobsAsync()
    {
        FlushSave();
        var enabledJobs = Jobs.Where(j => j.Enabled).ToList();
        if (!enabledJobs.Any())
        {
            MessageBox.Show("No enabled jobs found to run.", "Run All", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsRunning = true;
        AppendLog($"========== Starting Run All ({enabledJobs.Count} enabled jobs) ==========");

        int successCount = 0;
        int failedCount = 0;
        long grandTotalRows = 0;
        var totalFiles = 0;

        try
        {
            for (int i = 0; i < enabledJobs.Count; i++)
            {
                var jobVm = enabledJobs[i];
                AutoSaveStatus = $"Running job {i + 1}/{enabledJobs.Count}: '{jobVm.Name}'...";
                AppendLog($"----- [{i + 1}/{enabledJobs.Count}] Running '{jobVm.Name}' -----");

                try
                {
                    var jobModel = jobVm.ToModel();
                    var connModel = Connections.FirstOrDefault(c => c.Id == jobModel.ConnectionId)?.ToModel();

                    var result = await Task.Run(() => _exportService.ExecuteJobAsync(
                        jobModel,
                        connModel!,
                        msg => System.Windows.Application.Current.Dispatcher.Invoke(() => AppendLog(msg))));

                    successCount++;
                    grandTotalRows += result.TotalRows;
                    totalFiles += result.GeneratedFiles.Count;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    AppendLog($"ERROR in job '{jobVm.Name}':\n{ex}");
                    Logger.Error(ex, "Run All failed on job '{0}'", jobVm.Name);

                    var choice = MessageBox.Show(
                        $"Job '{jobVm.Name}' failed:\n\n{ex.Message}\n\nDo you want to continue running the remaining jobs?",
                        "Job Failed",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Error);

                    if (choice == MessageBoxResult.No)
                    {
                        AppendLog("Run All aborted by user.");
                        break;
                    }
                }
            }

            AutoSaveStatus = $"Run All finished. Success: {successCount}, Failed: {failedCount}.";
            AppendLog($"========== Run All Completed: {successCount} succeeded, {failedCount} failed, {grandTotalRows:N0} total rows across {totalFiles} files. ==========");

            MessageBox.Show(
                $"Run All Finished!\n\n" +
                $"Succeeded: {successCount}\n" +
                $"Failed: {failedCount}\n" +
                $"Total Rows: {grandTotalRows:N0}\n" +
                $"Total Files: {totalFiles}",
                "Run All Completed",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void AddJob()
    {
        string defaultFolder = string.IsNullOrWhiteSpace(DefaultExportFolder)
            ? AppStateRepository.DefaultExportDirectory
            : DefaultExportFolder;

        try
        {
            if (!Directory.Exists(defaultFolder))
            {
                Directory.CreateDirectory(defaultFolder);
            }
        }
        catch { }

        var newJob = new ExportJob
        {
            Name = $"Job {Jobs.Count + 1}",
            ConnectionId = Connections.FirstOrDefault()?.Id,
            OutputFolder = defaultFolder,
            BaseFileName = "ExportData"
        };

        var vm = new JobItemViewModel(newJob, TriggerChange);
        Jobs.Add(vm);
        SelectedJob = vm;
        AppendLog($"Added new job: {vm.Name}");
    }

    private void DeleteJob()
    {
        if (SelectedJob == null) return;

        string name = SelectedJob.Name;
        int idx = Jobs.IndexOf(SelectedJob);
        Jobs.Remove(SelectedJob);
        SelectedJob = Jobs.ElementAtOrDefault(idx) ?? Jobs.LastOrDefault();
        AppendLog($"Deleted job: {name}");
    }

    private void DuplicateJob()
    {
        if (SelectedJob == null) return;

        var clonedModel = SelectedJob.ToModel().Clone();
        var vm = new JobItemViewModel(clonedModel, TriggerChange);
        Jobs.Add(vm);
        SelectedJob = vm;
        AppendLog($"Duplicated job: {vm.Name}");
    }

    private void BrowseOutputFolder()
    {
        if (SelectedJob == null) return;

        using var dialog = new System.Windows.Forms.FolderBrowserDialog();
        dialog.Description = "Select Output Folder for Export Job";
        dialog.UseDescriptionForTitle = true;

        if (!string.IsNullOrWhiteSpace(SelectedJob.OutputFolder) && Directory.Exists(SelectedJob.OutputFolder))
        {
            dialog.SelectedPath = SelectedJob.OutputFolder;
        }

        var result = dialog.ShowDialog();
        if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            SelectedJob.OutputFolder = dialog.SelectedPath;
            AppendLog($"Updated output folder for '{SelectedJob.Name}' to '{dialog.SelectedPath}'");
        }
    }

    private void AddConnection()
    {
        var newProfile = new ConnectionProfile
        {
            Name = $"Connection {Connections.Count + 1}",
            Type = ConnectionType.Dsn,
            DsnName = "EDWHIVEPROD",
            Username = string.Empty,
            Password = string.Empty,
            RawConnectionString = "Driver={ODBC Driver 18 for SQL Server};Server=localhost;Database=master;Trusted_Connection=Yes;TrustServerCertificate=Yes;"
        };

        var vm = new ConnectionItemViewModel(newProfile, TriggerChange);
        Connections.Add(vm);
        SelectedConnection = vm;
        AppendLog($"Added new connection profile: {vm.Name} (Type: {vm.Type})");
    }

    private void DeleteConnection()
    {
        if (SelectedConnection == null) return;

        string name = SelectedConnection.Name;
        int idx = Connections.IndexOf(SelectedConnection);
        Connections.Remove(SelectedConnection);
        SelectedConnection = Connections.ElementAtOrDefault(idx) ?? Connections.LastOrDefault();
        AppendLog($"Deleted connection profile: {name}");
    }

    private void TestConnection()
    {
        if (SelectedConnection == null) return;

        var profile = SelectedConnection.ToModel();
        string maskedStr;
        try
        {
            maskedStr = profile.BuildOdbcConnectionString(maskPassword: true);
        }
        catch
        {
            maskedStr = profile.Type == ConnectionType.Dsn ? $"DSN={profile.DsnName ?? "<empty>"}" : (profile.RawConnectionString ?? "<empty>");
        }

        AppendLog($"Testing connection '{SelectedConnection.Name}' ({maskedStr})...");
        var (success, message) = OdbcConnectionTester.TestConnection(profile);

        AppendLog($"Connection test result: {message}");

        if (success)
        {
            MessageBox.Show(message, "Connection Test Succeeded", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(message, "Connection Test Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BrowseDefaultExportFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog();
        dialog.Description = "Select Default Export Folder";
        dialog.UseDescriptionForTitle = true;

        if (!string.IsNullOrWhiteSpace(DefaultExportFolder) && Directory.Exists(DefaultExportFolder))
        {
            dialog.SelectedPath = DefaultExportFolder;
        }

        var result = dialog.ShowDialog();
        if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            DefaultExportFolder = dialog.SelectedPath;
            AppendLog($"Updated default export folder to '{dialog.SelectedPath}'");
        }
    }

    private void OpenFolder(string? folderPath, string folderLabel)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                MessageBox.Show($"No {folderLabel} path is specified.", "Open Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppendLog($"Failed to open {folderLabel} ('{folderPath}'): {ex.Message}");
            MessageBox.Show($"Could not open {folderLabel}:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void AppendLog(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        string entry = $"[{timestamp}] {message}{Environment.NewLine}";
        LogText += entry;
        Logger.Info(message);
    }
}
