using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitLabDesktop.Core.Git;
using GitLabDesktop.Core.Hosting;
using GitLabDesktop.Services;

namespace GitLabDesktop.ViewModels;

/// <summary>Options: GitLab/GitHub accounts, repositories folder, git and editor paths. Completes with true when saved.</summary>
public sealed partial class SettingsViewModel : ModalViewModel<bool>
{
    readonly AppSettings _settings;
    readonly HostingRegistry _hosting;

    public SettingsViewModel(AppSettings settings, HostingRegistry hosting)
    {
        _settings = settings;
        _hosting = hosting;
        foreach (var a in settings.Accounts) Accounts.Add(new AccountViewModel(a, hosting, RemoveAccount));
        _repositoriesDirectory = settings.RepositoriesDirectory;
        _autoAddRepositories = settings.AutoAddRepositories;
        _cloneWithSsh = settings.CloneWithSsh;
        _autoCheckUpdates = AppUpdater.AutoCheck;
        _includePrereleases = AppUpdater.IncludePrereleases;
        _ = UpdateFoundRepositoriesAsync();
        _editorCommand = settings.EditorCommand;
        _gitExecutable = settings.GitExecutable;
        Accounts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanAddGitHub));
        foreach (var login in settings.SavedGitLogins.OrderBy(l => l.Scope)) GitLogins.Add(login);
    }

    protected override bool CancelledResult => false;

    [ObservableProperty] private string _repositoriesDirectory;
    [ObservableProperty] private bool _autoAddRepositories;
    [ObservableProperty] private string? _foundRepositoriesText;
    [ObservableProperty] private bool _cloneWithSsh;
    [ObservableProperty] private bool _autoCheckUpdates;
    [ObservableProperty] private bool _includePrereleases;
    [ObservableProperty] private string _editorCommand;
    [ObservableProperty] private string _gitExecutable;
    [ObservableProperty] private string? _gitTestResult;

    // ── Accounts ─────────────────────────────────────────────────────────────

    public ObservableCollection<AccountViewModel> Accounts { get; } = [];

    /// <summary>One GitHub account (github.com); any number of GitLab servers.</summary>
    public bool CanAddGitHub => Accounts.All(a => a.Kind != HostingKind.GitHub);

    [RelayCommand]
    void AddGitLabAccount()
        => Accounts.Add(new AccountViewModel(new HostAccount { Kind = HostingKind.GitLab, UseTokenForGit = true }, _hosting, RemoveAccount));

    [RelayCommand]
    void AddGitHubAccount()
    {
        if (CanAddGitHub)
            Accounts.Add(new AccountViewModel(new HostAccount { Kind = HostingKind.GitHub, BaseUrl = AppSettings.GitHubUrl }, _hosting, RemoveAccount));
    }

    void RemoveAccount(AccountViewModel account) => Accounts.Remove(account);

    // ── Saved git sign-ins (from the sign-in dialog) ─────────────────────────

    public ObservableCollection<AppSettings.GitLogin> GitLogins { get; } = [];
    public bool HasGitLogins => GitLogins.Count > 0;

    public string CurrentVersionText => $"Installed version: {AppUpdater.CurrentVersion}. Help › Check for updates checks now.";

    /// <summary>Forgets immediately (not on Save): the stored secret is removed from secure storage.</summary>
    [RelayCommand]
    void ForgetGitLogin(AppSettings.GitLogin login)
    {
        _settings.ForgetGitLogin(login);
        GitLogins.Remove(login);
        OnPropertyChanged(nameof(HasGitLogins));
    }

    [RelayCommand]
    async Task TestGit()
    {
        try
        {
            var r = await new GitRunner { GitExecutable = GitExecutable }.RunAsync(Environment.CurrentDirectory, ["--version"]);
            GitTestResult = $"✓ {r.StdOut.Trim()}";
        }
        catch (Exception ex)
        {
            GitTestResult = $"✗ {ex.Message}";
        }
    }

    // ── Repositories folder ──────────────────────────────────────────────────

    CancellationTokenSource? _scanCts;

    /// <summary>GitHub Desktop's default clone folder, offered as a one-click choice when it exists.</summary>
    public string GitHubDesktopFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GitHub");

    public bool HasGitHubDesktopFolder => Directory.Exists(GitHubDesktopFolder);

    partial void OnRepositoriesDirectoryChanged(string value) => _ = UpdateFoundRepositoriesAsync();

    /// <summary>Shows how many existing checkouts the folder contains (debounced while typing).</summary>
    async Task UpdateFoundRepositoriesAsync()
    {
        _scanCts?.Cancel();
        var cts = _scanCts = new CancellationTokenSource();
        var dir = RepositoriesDirectory.Trim();
        try
        {
            await Task.Delay(300, cts.Token);
            if (!Directory.Exists(dir))
            {
                FoundRepositoriesText = "This folder does not exist yet; it will be created when you clone into it.";
                return;
            }
            var found = await Task.Run(() => RepositoryScanner.FindRepositories(dir), cts.Token);
            if (cts.IsCancellationRequested) return;
            FoundRepositoriesText = found.Count switch
            {
                0 => "No repositories found in this folder.",
                1 => "1 repository found in this folder.",
                var n => $"{n} repositories found in this folder.",
            };
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand]
    async Task BrowseRepositoriesDirectory()
    {
        try
        {
            var start = Directory.Exists(RepositoriesDirectory) ? RepositoriesDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var result = await CommunityToolkit.Maui.Storage.FolderPicker.Default.PickAsync(start, CancellationToken.None);
            if (result.IsSuccessful) RepositoriesDirectory = result.Folder.Path;
        }
        catch (Exception ex)
        {
            FoundRepositoriesText = ex.Message;
        }
    }

    public string DefaultRepositoriesDirectory => AppSettings.DefaultRepositoriesDirectory;

    [RelayCommand]
    void UseGitHubDesktopFolder() => RepositoriesDirectory = GitHubDesktopFolder;

    [RelayCommand]
    void UseDefaultRepositoriesDirectory() => RepositoriesDirectory = DefaultRepositoriesDirectory;

    [RelayCommand]
    async Task Save()
    {
        var accounts = Accounts.Select(a => a.ToAccount()).ToList();
        var bad = accounts.FirstOrDefault(a => a.Kind == HostingKind.GitLab && !Uri.TryCreate(a.BaseUrl, UriKind.Absolute, out _));
        if (bad is not null)
        {
            Error = "Each GitLab account needs a server URL, e.g. https://gitlab.example.com.";
            return;
        }
        var duplicate = accounts.GroupBy(a => a.Host, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            Error = $"There is more than one account for {duplicate.Key}.";
            return;
        }

        try
        {
            await _settings.SaveAccountsAsync(accounts);
        }
        catch (Exception ex)
        {
            // e.g. the macOS keychain refusing an unsigned app
            Error = $"Could not store the access tokens securely: {ex.Message}";
            return;
        }
        _settings.RepositoriesDirectory = RepositoriesDirectory.Trim();
        _settings.AutoAddRepositories = AutoAddRepositories;
        _settings.CloneWithSsh = CloneWithSsh;
        AppUpdater.AutoCheck = AutoCheckUpdates;
        AppUpdater.IncludePrereleases = IncludePrereleases;
        _settings.EditorCommand = EditorCommand.Trim();
        _settings.GitExecutable = GitExecutable;
        Complete(true);
    }

    [RelayCommand]
    void Close() => Cancel();
}
