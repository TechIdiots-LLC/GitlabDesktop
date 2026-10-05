using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitLabDesktop.Core.Git;
using GitLabDesktop.Core.Hosting;
using GitLabDesktop.Services;

namespace GitLabDesktop.ViewModels;

/// <summary>Options: accounts, repositories folder, git and editor paths. Completes with true when saved.</summary>
public sealed partial class SettingsViewModel : ModalViewModel<bool>
{
    readonly AppSettings _settings;
    readonly HostingRegistry _hosting;

    readonly GitRunner _git;
    bool? _savedRecurseSubmodules, _savedLongPaths;

    public SettingsViewModel(AppSettings settings, HostingRegistry hosting, GitRunner git)
    {
        _settings = settings;
        _hosting = hosting;
        _git = git;
        _ = LoadGitDefaultsAsync();
        foreach (var a in settings.Accounts.OrderBy(a => a.Host, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Repository is not null))
            Accounts.Add(new AccountViewModel(a, hosting, RemoveAccount));
        _repositoriesDirectory = settings.RepositoriesDirectory;
        _autoAddRepositories = settings.AutoAddRepositories;
        _cloneWithSsh = settings.CloneWithSsh;
        _autoCheckUpdates = AppUpdater.AutoCheck;
        _includePrereleases = AppUpdater.IncludePrereleases;
        _ = UpdateFoundRepositoriesAsync();
        _editorCommand = settings.EditorCommand;
        _gitExecutable = settings.GitExecutable;
        Accounts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanAddGitHub));
    }

    protected override bool CancelledResult => false;

    [ObservableProperty] private string _repositoriesDirectory;
    [ObservableProperty] private bool _autoAddRepositories;
    [ObservableProperty] private string? _foundRepositoriesText;
    [ObservableProperty] private bool _cloneWithSsh;
    [ObservableProperty] private bool _autoCheckUpdates;
    [ObservableProperty] private bool _includePrereleases;
    [ObservableProperty] private string _editorCommand;
    /// <summary>
    /// git's own global submodule.recurse: checkout, switch, pull and fetch update submodules, and clones include them by
    /// default (git doesn't apply it to clone, so the clone dialog does).
    /// </summary>
    [ObservableProperty] private bool _recurseSubmodules;

    /// <summary>
    /// Git for Windows' core.longpaths: lets git create, check out and clone paths over 260 characters. Its installer
    /// often turns it on in the system config; this shows and changes what git uses.
    /// </summary>
    [ObservableProperty] private bool _longPaths;

    public bool ShowLongPaths => OperatingSystem.IsWindows();

    async Task LoadGitDefaultsAsync()
    {
        RecurseSubmodules = await GitRepository.GetDefaultBoolAsync(_git, "submodule.recurse") ?? false;
        _savedRecurseSubmodules = RecurseSubmodules;
        if (ShowLongPaths)
        {
            LongPaths = await GitRepository.GetDefaultBoolAsync(_git, "core.longpaths") ?? false;
            _savedLongPaths = LongPaths;
        }
    }

    [ObservableProperty] private string _gitExecutable;
    [ObservableProperty] private string? _gitTestResult;

    // ── Accounts ─────────────────────────────────────────────────────────────

    public ObservableCollection<AccountViewModel> Accounts { get; } = [];

    /// <summary>One github.com account; any number of GitLab servers.</summary>
    public bool CanAddGitHub => Accounts.All(a => !(a.Kind == HostingKind.GitHub && a.UsesApi));

    [RelayCommand]
    void AddGitLabAccount()
        => Accounts.Add(new AccountViewModel(new HostAccount { Kind = HostingKind.GitLab }, _hosting, RemoveAccount));

    [RelayCommand]
    void AddGitHubAccount()
    {
        if (CanAddGitHub)
            Accounts.Add(new AccountViewModel(new HostAccount { Kind = HostingKind.GitHub, BaseUrl = AppSettings.GitHubUrl }, _hosting, RemoveAccount));
    }

    /// <summary>Any other server git reaches over HTTPS: a sign-in for git only, with no merge requests or CI.</summary>
    [RelayCommand]
    void AddGitServerAccount()
        => Accounts.Add(new AccountViewModel(new HostAccount { Kind = HostingKind.Unknown }, _hosting, RemoveAccount));

    void RemoveAccount(AccountViewModel account) => Accounts.Remove(account);

    public string CurrentVersionText => $"Installed version: {AppUpdater.CurrentVersion}. Help › Check for updates checks now.";

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
        // github.com accounts always have their URL; every other whole-server account needs one typed in.
        var bad = Accounts.FirstOrDefault(a => a.IsEditableServer && !Uri.TryCreate(a.NormalizedUrl, UriKind.Absolute, out _));
        if (bad is not null)
        {
            Error = $"Each {bad.KindName} account needs a server URL, e.g. {bad.ServerPlaceholder}.";
            return;
        }
        var duplicate = accounts.GroupBy(a => a.Repository ?? a.Host, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            Error = $"There is more than one account for {duplicate.First().Title}.";
            return;
        }

        try
        {
            await _settings.SaveAccountsAsync(accounts);
        }
        catch (Exception ex)
        {
            // e.g. the macOS keychain refusing an unsigned app
            Error = $"Could not store the passwords and tokens securely: {ex.Message}";
            return;
        }
        _settings.RepositoriesDirectory = RepositoriesDirectory.Trim();
        _settings.AutoAddRepositories = AutoAddRepositories;
        _settings.CloneWithSsh = CloneWithSsh;
        AppUpdater.AutoCheck = AutoCheckUpdates;
        AppUpdater.IncludePrereleases = IncludePrereleases;
        _settings.EditorCommand = EditorCommand.Trim();
        _settings.GitExecutable = GitExecutable;
        foreach (var (key, before, now) in new[]
                 {
                     ("submodule.recurse", _savedRecurseSubmodules, RecurseSubmodules),
                     ("core.longpaths", _savedLongPaths, LongPaths),
                 })
        {
            if (before is null || before == now) continue;   // not loaded (or not on Windows), or unchanged
            try { await GitRepository.SetDefaultBoolAsync(_git, key, now); }
            catch (Exception ex)
            {
                Error = $"Could not change git's {key} setting: {ex.Message}";
                return;
            }
        }
        Complete(true);
    }

    [RelayCommand]
    void Close() => Cancel();
}
