using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using CyberGemini.Models;
using CyberGemini.Services;
using CyberGemini.Utilities;
using MessageBox = System.Windows.MessageBox;

namespace CyberGemini.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly FileScannerService _scanner = new();
    private readonly SettingsService _settingsService = new();
    private PauseTokenSource? _pauseTokenSource;
    private bool _isScanning;
    private bool _isPaused;
    private string _statusMessage = "Ready";
    private double _progress;
    private string? _selectedScanPath;
    private bool _createBackupBeforeDelete;
    private int _maxDegreeOfParallelism = Environment.ProcessorCount;
    private string _selectedHashAlgorithm = "SHA256";
    private bool _skipHiddenFiles;
    private bool _skipSystemFiles;
    private string _excludedExtensionsText = string.Empty;
    private double _minFileSizeMb;
    private double _maxFileSizeMb;
    private bool _folderSafetyEnabled = true;

    public ObservableCollection<string> ScanPaths { get; } = new();
    public ObservableCollection<DuplicateFileItem> DuplicateFiles { get; } = new();
    public ObservableCollection<NameDuplicateItem> NameDuplicates { get; } = new();
    public ObservableCollection<string> HashAlgorithms { get; } = new() { "SHA256", "SHA384", "SHA512", "SHA1" };

    public RelayCommand AddFolderCommand { get; }
    public RelayCommand RemoveFolderCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public AsyncRelayCommand DeleteRecycleCommand { get; }
    public AsyncRelayCommand DeletePermanentCommand { get; }
    public AsyncRelayCommand MoveSelectedCommand { get; }
    public AsyncRelayCommand SaveRenamesCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }

    public MainViewModel()
    {
        AddFolderCommand = new RelayCommand(AddFolder);
        RemoveFolderCommand = new RelayCommand(RemoveFolder, () => !string.IsNullOrWhiteSpace(SelectedScanPath));
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !_isScanning);
        SelectAllCommand = new RelayCommand(SelectAllDuplicates);
        ClearSelectionCommand = new RelayCommand(ClearSelections);
        DeleteRecycleCommand = new AsyncRelayCommand(() => DeleteSelectedAsync(permanent: false));
        DeletePermanentCommand = new AsyncRelayCommand(() => DeleteSelectedAsync(permanent: true));
        MoveSelectedCommand = new AsyncRelayCommand(MoveSelectedAsync);
        SaveRenamesCommand = new AsyncRelayCommand(SaveRenamesAsync);
        PauseCommand = new RelayCommand(PauseScan, () => _isScanning && !_isPaused);
        ResumeCommand = new RelayCommand(ResumeScan, () => _isScanning && _isPaused);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);

        DuplicateFiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(DuplicateSummary));
            OnPropertyChanged(nameof(SelectedSizeSummary));
            // Subscribe to IsSelected changes on new items
            foreach (var item in DuplicateFiles)
            {
                item.PropertyChanged -= OnDuplicateItemPropertyChanged;
                item.PropertyChanged += OnDuplicateItemPropertyChanged;
            }
        };
        NameDuplicates.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NameDuplicateSummary));

        LoadSettings();
        UpdateStatusBadge();
    }

    public string? SelectedScanPath
    {
        get => _selectedScanPath;
        set
        {
            if (SetProperty(ref _selectedScanPath, value))
            {
                RemoveFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CreateBackupBeforeDelete
    {
        get => _createBackupBeforeDelete;
        set => SetProperty(ref _createBackupBeforeDelete, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public int MaxParallelismLimit => Environment.ProcessorCount;

    public int MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        set => SetProperty(ref _maxDegreeOfParallelism, Math.Max(1, value));
    }

    public string SelectedHashAlgorithm
    {
        get => _selectedHashAlgorithm;
        set => SetProperty(ref _selectedHashAlgorithm, value);
    }

    public bool SkipHiddenFiles
    {
        get => _skipHiddenFiles;
        set => SetProperty(ref _skipHiddenFiles, value);
    }

    public bool SkipSystemFiles
    {
        get => _skipSystemFiles;
        set => SetProperty(ref _skipSystemFiles, value);
    }

    public string ExcludedExtensionsText
    {
        get => _excludedExtensionsText;
        set => SetProperty(ref _excludedExtensionsText, value);
    }

    public double MinFileSizeMb
    {
        get => _minFileSizeMb;
        set => SetProperty(ref _minFileSizeMb, Math.Max(0, value));
    }

    public double MaxFileSizeMb
    {
        get => _maxFileSizeMb;
        set => SetProperty(ref _maxFileSizeMb, Math.Max(0, value));
    }

    public bool FolderSafetyEnabled
    {
        get => _folderSafetyEnabled;
        set => SetProperty(ref _folderSafetyEnabled, value);
    }

    public string StatusBadgeText => _isScanning ? (_isPaused ? "Paused" : "Scanning") : "Ready";

    public System.Windows.Media.Brush StatusBadgeBrush => _isScanning
        ? (_isPaused
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(249, 115, 22))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 194, 255)))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128));

    public string DuplicateSummary
        => $"{DuplicateFiles.Count} duplicate files across {DuplicateFiles.Select(item => item.GroupId).Distinct().Count()} groups";

    public string SelectedSizeSummary
    {
        get
        {
            var selected = DuplicateFiles.Where(f => f.IsSelected).ToList();
            if (selected.Count == 0)
                return "No files selected";
            var totalBytes = selected.Sum(f => f.Size);
            return $"{selected.Count} files selected — {FormatBytes(totalBytes)}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:F2} {units[unit]}";
    }

    public string NameDuplicateSummary
        => $"{NameDuplicates.Count} files share a name (renaming recommended)";

    private void AddFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a folder to scan",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            if (!ScanPaths.Contains(dialog.SelectedPath))
            {
                ScanPaths.Add(dialog.SelectedPath);
            }
        }
    }

    private void RemoveFolder()
    {
        if (!string.IsNullOrWhiteSpace(SelectedScanPath))
        {
            ScanPaths.Remove(SelectedScanPath);
        }
    }

    private async Task ScanAsync()
    {
        if (!ScanPaths.Any())
        {
            MessageBox.Show("Add at least one folder before scanning.", "No folders", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _isScanning = true;
        _isPaused = false;
        _pauseTokenSource = new PauseTokenSource();
        ScanCommand.RaiseCanExecuteChanged();
        PauseCommand.RaiseCanExecuteChanged();
        ResumeCommand.RaiseCanExecuteChanged();

        StatusMessage = "Scanning...";
        Progress = 0;
        DuplicateFiles.Clear();
        NameDuplicates.Clear();
        UpdateStatusBadge();

        var progress = new Progress<FileScannerService.ScanProgress>(value =>
        {
            Progress = value.Percent;
            StatusMessage = value.Message + (_isPaused ? " (Paused)" : string.Empty);
        });

        var options = new FileScannerService.ScanOptions
        {
            MaxDegreeOfParallelism = MaxDegreeOfParallelism,
            HashAlgorithm = new HashAlgorithmName(SelectedHashAlgorithm),
            SkipHiddenFiles = SkipHiddenFiles,
            SkipSystemFiles = SkipSystemFiles,
            ExcludedExtensions = ParseExtensions(ExcludedExtensionsText),
            MinFileSizeBytes = MinFileSizeMb <= 0 ? null : (long?)(MinFileSizeMb * 1024 * 1024),
            MaxFileSizeBytes = MaxFileSizeMb <= 0 ? null : (long?)(MaxFileSizeMb * 1024 * 1024),
            PauseToken = _pauseTokenSource.Token
        };

        // Stream duplicates to the UI as they are found
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        Action<DuplicateFileItem> onDuplicateFound = item =>
        {
            dispatcher.Invoke(() =>
            {
                item.PropertyChanged -= OnDuplicateItemPropertyChanged;
                item.PropertyChanged += OnDuplicateItemPropertyChanged;
                DuplicateFiles.Add(item);
            });
        };

        var result = await _scanner.ScanAsync(ScanPaths, options, progress, onDuplicateFound, CancellationToken.None);

        // Name duplicates are still added after scan completes
        foreach (var item in result.NameDuplicates)
        {
            NameDuplicates.Add(item);
        }

        // Refresh folder-essential flags on the UI thread
        // (scanner computed them, but streamed items may need a property-changed notification)
        foreach (var item in DuplicateFiles)
        {
            // Trigger UI refresh for IsFolderEssential (was set by scanner after streaming)
            item.OnPropertyChanged(nameof(DuplicateFileItem.IsFolderEssential));
        }

        StatusMessage = "Scan complete.";
        _isScanning = false;
        _isPaused = false;
        ScanCommand.RaiseCanExecuteChanged();
        PauseCommand.RaiseCanExecuteChanged();
        ResumeCommand.RaiseCanExecuteChanged();
        UpdateStatusBadge();
    }

    private void PauseScan()
    {
        _pauseTokenSource?.Pause();
        _isPaused = true;
        StatusMessage = "Scanning paused.";
        PauseCommand.RaiseCanExecuteChanged();
        ResumeCommand.RaiseCanExecuteChanged();
        UpdateStatusBadge();
    }

    private void ResumeScan()
    {
        _pauseTokenSource?.Resume();
        _isPaused = false;
        StatusMessage = "Scanning resumed.";
        PauseCommand.RaiseCanExecuteChanged();
        ResumeCommand.RaiseCanExecuteChanged();
        UpdateStatusBadge();
    }

    private void SelectAllDuplicates()
    {
        foreach (var item in DuplicateFiles)
        {
            // When Folder Safety is on, skip files that are essential to their folder
            if (FolderSafetyEnabled && item.IsFolderEssential)
            {
                item.IsSelected = false;
                continue;
            }
            item.IsSelected = true;
        }
    }

    private void ClearSelections()
    {
        foreach (var item in DuplicateFiles)
        {
            item.IsSelected = false;
        }
    }

    private async Task DeleteSelectedAsync(bool permanent)
    {
        var targets = DuplicateFiles.Where(item => item.IsSelected).ToList();
        if (!targets.Any())
        {
            MessageBox.Show("Select at least one duplicate file before deleting.", "Nothing selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (permanent)
        {
            var result = MessageBox.Show(
                "Permanent delete cannot be undone. Continue?",
                "Permanent delete warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        if (CreateBackupBeforeDelete)
        {
            var backupFolder = ChooseFolder("Select backup destination");
            if (string.IsNullOrWhiteSpace(backupFolder))
            {
                return;
            }

            var backupPath = Path.Combine(backupFolder, $"CyberGemini_Backup_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(backupPath);
            foreach (var item in targets)
            {
                await CopyFileAsync(item.FullPath, backupPath);
            }
        }

        foreach (var item in targets)
        {
            try
            {
                if (permanent)
                {
                    File.Delete(item.FullPath);
                }
                else
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        item.FullPath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to delete {item.FullPath}. {ex.Message}", "Delete error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        var removed = targets.Count;
        foreach (var item in targets)
        {
            DuplicateFiles.Remove(item);
        }

        StatusMessage = $"Deleted {removed} files.";
    }

    private async Task MoveSelectedAsync()
    {
        var targets = DuplicateFiles.Where(item => item.IsSelected).ToList();
        if (!targets.Any())
        {
            MessageBox.Show("Select at least one duplicate file before moving.", "Nothing selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var destinationRoot = ChooseFolder("Select destination folder");
        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            return;
        }

        var destinationFolder = Path.Combine(destinationRoot, $"CyberGemini_Moved_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(destinationFolder);

        foreach (var item in targets)
        {
            try
            {
                var targetPath = GetUniquePath(destinationFolder, Path.GetFileName(item.FullPath));
                File.Move(item.FullPath, targetPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to move {item.FullPath}. {ex.Message}", "Move error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        foreach (var item in targets)
        {
            DuplicateFiles.Remove(item);
        }

        StatusMessage = $"Moved {targets.Count} files.";
        await Task.CompletedTask;
    }

    private async Task SaveRenamesAsync()
    {
        var renameTargets = NameDuplicates
            .Where(item => !string.IsNullOrWhiteSpace(item.NewFileName))
            .Where(item => !string.Equals(item.NewFileName, item.FileName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!renameTargets.Any())
        {
            MessageBox.Show("Enter new file names before saving.", "No changes", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var item in renameTargets)
        {
            try
            {
                var directory = Path.GetDirectoryName(item.FullPath) ?? string.Empty;
                var newPath = Path.Combine(directory, item.NewFileName);
                if (File.Exists(newPath))
                {
                    MessageBox.Show($"{newPath} already exists.", "Rename error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }

                File.Move(item.FullPath, newPath);
                item.FullPath = newPath;
                item.FileName = item.NewFileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to rename {item.FullPath}. {ex.Message}", "Rename error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        StatusMessage = "Rename complete.";
        await Task.CompletedTask;
    }

    private async Task SaveSettingsAsync()
    {
        var settings = new AppSettings
        {
            MaxDegreeOfParallelism = MaxDegreeOfParallelism,
            HashAlgorithm = SelectedHashAlgorithm,
            SkipHiddenFiles = SkipHiddenFiles,
            SkipSystemFiles = SkipSystemFiles,
            ExcludedExtensions = ExcludedExtensionsText,
            MinFileSizeMb = MinFileSizeMb,
            MaxFileSizeMb = MaxFileSizeMb,
            FolderSafetyEnabled = FolderSafetyEnabled
        };

        await _settingsService.SaveAsync(settings);
        StatusMessage = "Settings saved.";
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        if (settings is null)
        {
            return;
        }

        MaxDegreeOfParallelism = settings.MaxDegreeOfParallelism;
        SelectedHashAlgorithm = settings.HashAlgorithm;
        SkipHiddenFiles = settings.SkipHiddenFiles;
        SkipSystemFiles = settings.SkipSystemFiles;
        ExcludedExtensionsText = settings.ExcludedExtensions;
        MinFileSizeMb = settings.MinFileSizeMb;
        MaxFileSizeMb = settings.MaxFileSizeMb;
        FolderSafetyEnabled = settings.FolderSafetyEnabled;
    }

    private void OnDuplicateItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DuplicateFileItem.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedSizeSummary));
        }
    }

    private void UpdateStatusBadge()
    {
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(StatusBadgeBrush));
    }

    private static IReadOnlySet<string> ParseExtensions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var items = text.Split(new[] { ',', ';', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Select(item => item.StartsWith('.') ? item : "." + item)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return items;
    }

    private static string? ChooseFolder(string title)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }

    private static Task CopyFileAsync(string filePath, string destinationFolder)
    {
        var targetPath = GetUniquePath(destinationFolder, Path.GetFileName(filePath));
        return Task.Run(() => File.Copy(filePath, targetPath));
    }

    private static string GetUniquePath(string folder, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(folder, fileName);
        var index = 1;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(folder, $"{baseName}_{index}{extension}");
            index++;
        }

        return candidate;
    }
}