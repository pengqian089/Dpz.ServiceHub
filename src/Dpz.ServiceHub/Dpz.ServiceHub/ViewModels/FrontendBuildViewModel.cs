using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dpz.ServiceHub.Models;
using Dpz.ServiceHub.Services;
using Serilog;

namespace Dpz.ServiceHub.ViewModels;

public sealed partial class FrontendBuildViewModel : ViewModelBase
{
    private readonly FrontendBuildStore _store = new();
    private readonly BuildRunner _buildRunner = new();
    private readonly S3UploadService _uploadService = new();
    private readonly FrontendBuildSettings _settings;
    private readonly StringBuilder _logBuilder = new();
    private CancellationTokenSource? _operationCts;
    private bool _suppressSelectionSync;

    public FrontendBuildViewModel()
    {
        _settings = _store.Load();
        foreach (var profile in _settings.Profiles)
        {
            Profiles.Add(profile);
        }

        if (Profiles.Count > 0)
        {
            SelectProfile(Profiles[0]);
        }
    }

    public ObservableCollection<FrontendBuildProfile> Profiles { get; } = [];

    public ObservableCollection<string> ArtifactPaths { get; } = [];

    public event EventHandler<string>? LogChunkReceived;

    public event EventHandler? LogReset;

    public string LogBuffer => _logBuilder.ToString();

    public Func<string, Task<UploadConfirmResult>>? RequestUploadConfirmationAsync { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildAndUploadCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveArtifactCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelectedProfile))]
    [NotifyPropertyChangedFor(nameof(IsEditorEnabled))]
    private FrontendBuildProfile? _selectedProfile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildAndUploadCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    [NotifyPropertyChangedFor(nameof(IsEditorEnabled))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isUploading;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _workingDirectory = string.Empty;

    [ObservableProperty]
    private string _executable = "pwsh";

    [ObservableProperty]
    private string _arguments = string.Empty;

    [ObservableProperty]
    private string _defaultRemotePrefix = string.Empty;

    [ObservableProperty]
    private string _s3Endpoint = string.Empty;

    [ObservableProperty]
    private string _s3Bucket = string.Empty;

    [ObservableProperty]
    private string _s3Region = string.Empty;

    [ObservableProperty]
    private string _accessKey = string.Empty;

    [ObservableProperty]
    private string _secretKey = string.Empty;

    [ObservableProperty]
    private bool _s3ForcePathStyle;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveArtifactCommand))]
    private string? _selectedArtifactPath;

    [ObservableProperty]
    private double _currentFileProgress;

    [ObservableProperty]
    private double _totalProgress;

    [ObservableProperty]
    private string _currentFileName = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    public bool HasSelectedProfile => SelectedProfile != null;

    public bool IsEditorEnabled => HasSelectedProfile && !IsBusy;

    partial void OnNameChanged(string value)
    {
        if (SelectedProfile != null)
        {
            SelectedProfile.Name = value;
        }
    }

    partial void OnSelectedProfileChanged(
        FrontendBuildProfile? oldValue,
        FrontendBuildProfile? newValue
    )
    {
        OnPropertyChanged(nameof(HasSelectedProfile));
        OnPropertyChanged(nameof(IsEditorEnabled));
        if (_suppressSelectionSync)
        {
            return;
        }

        if (oldValue != null)
        {
            ApplyEditorTo(oldValue);
        }

        LoadEditorFrom(newValue);
    }

    [RelayCommand]
    private void AddProfile()
    {
        if (SelectedProfile != null)
        {
            ApplyEditorTo(SelectedProfile);
        }

        var profile = new FrontendBuildProfile();
        _settings.Profiles.Add(profile);
        Profiles.Add(profile);
        SelectProfile(profile);
        Persist();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private void DeleteProfile()
    {
        if (SelectedProfile == null)
        {
            return;
        }

        var index = Profiles.IndexOf(SelectedProfile);
        _settings.Profiles.Remove(SelectedProfile);
        Profiles.Remove(SelectedProfile);

        FrontendBuildProfile? next = null;
        if (Profiles.Count > 0)
        {
            next = Profiles[Math.Clamp(index, 0, Profiles.Count - 1)];
        }

        SelectProfile(next);
        Persist();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedProfile != null)
        {
            ApplyEditorTo(SelectedProfile);
        }

        await _store.SaveAsync(_settings);
        AppendLog("配置已保存。");
    }

    public void Persist()
    {
        if (SelectedProfile != null)
        {
            ApplyEditorTo(SelectedProfile);
        }

        _store.Save(_settings);
    }

    public void AddArtifactPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var trimmed = path.Trim().Trim('"');
        if (
            ArtifactPaths.Any(existing =>
                string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return;
        }

        ArtifactPaths.Add(trimmed);
    }

    [RelayCommand(CanExecute = nameof(CanRemoveArtifact))]
    private void RemoveArtifact()
    {
        if (string.IsNullOrWhiteSpace(SelectedArtifactPath))
        {
            return;
        }

        ArtifactPaths.Remove(SelectedArtifactPath);
        SelectedArtifactPath = ArtifactPaths.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private Task BuildAsync()
    {
        return RunBuildAsync(uploadAfterBuild: false);
    }

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private Task BuildAndUploadAsync()
    {
        return RunBuildAsync(uploadAfterBuild: true);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _operationCts?.Cancel();
    }

    private bool CanBuild()
    {
        return !IsBusy && SelectedProfile != null;
    }

    private bool CanCancel()
    {
        return IsBusy;
    }

    private bool CanDeleteProfile()
    {
        return !IsBusy && SelectedProfile != null;
    }

    private bool CanRemoveArtifact()
    {
        return SelectedProfile != null && !string.IsNullOrWhiteSpace(SelectedArtifactPath);
    }

    private async Task RunBuildAsync(bool uploadAfterBuild)
    {
        if (SelectedProfile == null)
        {
            return;
        }

        ApplyEditorTo(SelectedProfile);
        Persist();

        if (string.IsNullOrWhiteSpace(WorkingDirectory) || !Directory.Exists(WorkingDirectory))
        {
            AppendLog("工作目录不存在，无法构建。");
            return;
        }

        if (string.IsNullOrWhiteSpace(Executable))
        {
            AppendLog("请填写可执行文件。");
            return;
        }

        IsBusy = true;
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        var cancellationToken = _operationCts.Token;

        try
        {
            ClearLog();
            AppendLog($"开始构建：{Name}");
            var exitCode = await _buildRunner.RunAsync(
                WorkingDirectory.Trim(),
                Executable.Trim(),
                Arguments,
                AppendLog,
                cancellationToken
            );

            if (exitCode != 0)
            {
                AppendLog($"构建失败，退出码 {exitCode}。");
                return;
            }

            AppendLog("构建成功。");

            var shouldUpload = uploadAfterBuild;
            var prefix = DefaultRemotePrefix;
            if (!uploadAfterBuild && RequestUploadConfirmationAsync != null)
            {
                var confirm = await RequestUploadConfirmationAsync(DefaultRemotePrefix);
                shouldUpload = confirm.ShouldUpload;
                prefix = confirm.Prefix;
            }

            if (shouldUpload)
            {
                await UploadArtifactsAsync(prefix, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("已取消。");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Frontend build or upload failed for profile {ProfileName}.", Name);
            AppendLog($"出错：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            IsUploading = false;
            _operationCts?.Dispose();
            _operationCts = null;
        }
    }

    private async Task UploadArtifactsAsync(
        string remotePrefix,
        CancellationToken cancellationToken
    )
    {
        if (
            string.IsNullOrWhiteSpace(S3Endpoint)
            || string.IsNullOrWhiteSpace(S3Bucket)
            || string.IsNullOrWhiteSpace(AccessKey)
            || string.IsNullOrWhiteSpace(SecretKey)
        )
        {
            AppendLog("S3 配置不完整，无法上传。");
            return;
        }

        if (ArtifactPaths.Count == 0)
        {
            AppendLog("未配置产物路径，无法上传。");
            return;
        }

        var files = S3UploadService.CollectArtifacts(ArtifactPaths);
        if (files.Count == 0)
        {
            AppendLog("产物路径下没有可上传的文件。");
            return;
        }

        IsUploading = true;
        CurrentFileProgress = 0;
        TotalProgress = 0;
        CurrentFileName = string.Empty;
        ProgressText = $"0/{files.Count}";
        AppendLog($"开始上传 {files.Count} 个文件到 {S3Endpoint} / {S3Bucket}。");

        var connection = new S3Connection(
            S3Endpoint.Trim(),
            S3Bucket.Trim(),
            string.IsNullOrWhiteSpace(S3Region) ? null : S3Region.Trim(),
            AccessKey.Trim(),
            SecretKey,
            S3ForcePathStyle
        );

        var progress = new Progress<S3UploadProgress>(update =>
        {
            CurrentFileName = update.CurrentFileName;
            CurrentFileProgress = update.CurrentFilePercent;
            TotalProgress = update.TotalPercent;
            ProgressText = $"{update.CompletedFiles}/{update.TotalFiles}";
        });

        await _uploadService.UploadAsync(
            connection,
            remotePrefix,
            files,
            progress,
            cancellationToken
        );

        CurrentFileProgress = 100;
        TotalProgress = 100;
        AppendLog("上传完成（已覆盖同名对象）。");
    }

    private void SelectProfile(FrontendBuildProfile? profile)
    {
        _suppressSelectionSync = true;
        try
        {
            SelectedProfile = profile;
            LoadEditorFrom(profile);
        }
        finally
        {
            _suppressSelectionSync = false;
        }
    }

    private void LoadEditorFrom(FrontendBuildProfile? profile)
    {
        if (profile == null)
        {
            Name = string.Empty;
            WorkingDirectory = string.Empty;
            Executable = "pwsh";
            Arguments = string.Empty;
            DefaultRemotePrefix = string.Empty;
            S3Endpoint = string.Empty;
            S3Bucket = string.Empty;
            S3Region = string.Empty;
            AccessKey = string.Empty;
            SecretKey = string.Empty;
            S3ForcePathStyle = false;
            ArtifactPaths.Clear();
            SelectedArtifactPath = null;
            return;
        }

        Name = profile.Name;
        WorkingDirectory = profile.WorkingDirectory;
        Executable = profile.Executable;
        Arguments = profile.Arguments;
        DefaultRemotePrefix = profile.DefaultRemotePrefix;
        S3Endpoint = profile.S3Endpoint;
        S3Bucket = profile.S3Bucket;
        S3Region = profile.S3Region;
        AccessKey = SecretProtector.Unprotect(profile.S3AccessKeyProtected);
        SecretKey = SecretProtector.Unprotect(profile.S3SecretKeyProtected);
        S3ForcePathStyle = profile.S3ForcePathStyle;

        ArtifactPaths.Clear();
        foreach (var path in profile.ArtifactPaths)
        {
            ArtifactPaths.Add(path);
        }

        SelectedArtifactPath = ArtifactPaths.FirstOrDefault();
    }

    private void ApplyEditorTo(FrontendBuildProfile profile)
    {
        profile.Name = Name.Trim();
        profile.WorkingDirectory = WorkingDirectory.Trim();
        profile.Executable = Executable.Trim();
        profile.Arguments = Arguments;
        profile.DefaultRemotePrefix = DefaultRemotePrefix.Trim();
        profile.S3Endpoint = S3Endpoint.Trim();
        profile.S3Bucket = S3Bucket.Trim();
        profile.S3Region = S3Region.Trim();
        profile.S3ForcePathStyle = S3ForcePathStyle;
        profile.ArtifactPaths = ArtifactPaths.ToList();

        if (!string.IsNullOrWhiteSpace(AccessKey))
        {
            profile.S3AccessKeyProtected = SecretProtector.Protect(AccessKey.Trim());
        }

        if (!string.IsNullOrWhiteSpace(SecretKey))
        {
            profile.S3SecretKeyProtected = SecretProtector.Protect(SecretKey);
        }
    }

    private void ClearLog()
    {
        _logBuilder.Clear();
        LogReset?.Invoke(this, EventArgs.Empty);
    }

    private void AppendLog(string line)
    {
        void Append()
        {
            var chunk = line.EndsWith('\n') ? line : line + "\n";
            _logBuilder.Append(chunk);
            LogChunkReceived?.Invoke(this, chunk);
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Append();
            return;
        }

        Dispatcher.UIThread.Post(Append);
    }
}

public sealed record UploadConfirmResult(bool ShouldUpload, string Prefix);
