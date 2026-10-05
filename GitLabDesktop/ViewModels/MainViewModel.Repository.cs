using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.Input;
using GitLabDesktop.Core.Git;
using GitLabDesktop.Services;
using GitLabDesktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GitLabDesktop.ViewModels;

// File and Repository menus: open/clone/create repositories, sync with the remote, open things on GitLab.
public sealed partial class MainViewModel
{
    static readonly object AddRepositoryMarker = new();
    static readonly object CloneRepositoryMarker = new();

    // ── Choosing repositories ────────────────────────────────────────────────

    [RelayCommand]
    async Task ChooseRepository()
    {
        PickerItem[] actions =
        [
            new("Add…", null, AddRepositoryMarker),
            new("Clone…", null, CloneRepositoryMarker),
        ];
        var items = Repositories
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(p => new PickerItem(Path.GetFileName(p), p, p,
                string.Equals(p, Repo?.Path, StringComparison.OrdinalIgnoreCase) ? "current" : null));

        var picked = await _dialogs.DropdownAsync<object>(DropdownAnchor.Repository, "Repositories", items, actions,
            "No repositories yet. Add or clone one.");
        if (picked == AddRepositoryMarker) await AddLocalRepository();
        else if (picked == CloneRepositoryMarker) await CloneRepository();
        else if (picked is string path && !string.Equals(path, Repo?.Path, StringComparison.OrdinalIgnoreCase))
            await RunAsync("Opening…", () => OpenRepositoryAsync(path), refresh: false);
    }

    async Task<string?> PickFolderAsync(string? initial = null)
    {
        try
        {
            var result = await FolderPicker.Default.PickAsync(initial ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), CancellationToken.None);
            return result.IsSuccessful ? result.Folder.Path : null;
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync("Folder picker", ex.Message);
            return null;
        }
    }

    [RelayCommand]
    async Task AddLocalRepository()
    {
        var folder = await PickFolderAsync(_settings.RepositoriesDirectory);
        if (folder is null) return;

        await RunAsync("Opening…", async () =>
        {
            var root = await GitRepository.FindRootAsync(_git, folder);
            if (root is null)
            {
                if (!await _dialogs.ConfirmAsync("Not a git repository", $"{folder} is not a git repository. Create one there?", "Create repository"))
                    return;
                await GitRepository.InitAsync(_git, folder);
                root = folder;
            }
            await OpenRepositoryAsync(root);
        }, refresh: false);
    }

    [RelayCommand]
    async Task NewRepository()
    {
        var parent = await PickFolderAsync(_settings.RepositoriesDirectory);
        if (parent is null) return;
        var name = await _dialogs.PromptAsync("New repository", $"Repository name (created in {parent}):", accept: "Create");
        if (string.IsNullOrWhiteSpace(name)) return;

        await RunAsync("Creating repository…", async () =>
        {
            var dir = Path.Combine(parent, name.Trim());
            Directory.CreateDirectory(dir);
            await GitRepository.InitAsync(_git, dir);
            await OpenRepositoryAsync(dir);
        }, refresh: false);
    }

    [RelayCommand]
    async Task CloneRepository()
    {
        var vm = _services.GetRequiredService<CloneViewModel>();
        await _dialogs.PushModalAsync(new ClonePage(vm));
        var path = await vm.Result;
        if (path is not null) await RunAsync("Opening…", () => OpenRepositoryAsync(path), refresh: false);
    }

    [RelayCommand]
    async Task RemoveRepository()
    {
        if (Repo is null) return;
        if (!await _dialogs.ConfirmAsync("Remove repository",
                $"Remove {Repo.Name} from the list? The files on disk are not deleted.", "Remove"))
            return;
        Repositories.Remove(Repo.Path);
        _settings.HiddenRepositories = [.. _settings.HiddenRepositories, Repo.Path];
        _settings.Repositories = [.. Repositories];
        _settings.LastRepository = null;
        Repo = null;
        Status = null;
        Remote = null;
        ChangeRequest = null;
        Ci = null;
        ChangedFiles.Clear();
        Commits.Clear();
        ChangeDiffLines = null;
        CommitDiffLines = null;
        ChangeImageDiff = null;
        CommitImageDiff = null;
        CommitFiles = null;
    }

    // ── Sync ─────────────────────────────────────────────────────────────────

    /// <summary>The toolbar button: publish, pull, push or fetch depending on the branch state.</summary>
    [RelayCommand]
    Task Sync() => Status switch
    {
        null => Task.CompletedTask,
        { Branch: not null, Upstream: null } => PushAsync(force: false),
        { Behind: > 0 } => Pull(),
        { Ahead: > 0 } => PushAsync(force: false),
        _ => Fetch(),
    };

    [RelayCommand]
    Task Push() => PushAsync(force: false);

    async Task PushAsync(bool force)
    {
        if (Repo is null || !await RequireBranchAsync()) return;
        var status = Status!;
        if (!force && status.Behind > 0 && status.Ahead > 0)
        {
            var choice = await _dialogs.ChooseAsync(
                $"{status.Branch} has diverged from {status.Upstream}",
                "Pull (merge remote changes)", "Force push (overwrite remote)");
            if (choice is null) return;
            if (choice.StartsWith("Pull")) { await Pull(); return; }
            force = true;
        }
        await RunAsync(status.Upstream is null ? "Publishing branch…" : "Pushing…", async () =>
        {
            await Repo.PushAsync(RemoteName, status.Branch!, setUpstream: status.Upstream is null, force);
            _lastFetched = DateTimeOffset.Now;
        });
        await LoadHostingInfoAsync();
    }

    [RelayCommand]
    Task Pull() => Repo is null ? Task.CompletedTask : RunAsync("Pulling…", async () =>
    {
        await Repo.PullAsync();
        _lastFetched = DateTimeOffset.Now;
    });

    [RelayCommand]
    async Task Fetch()
    {
        if (Repo is null) return;
        await RunAsync("Fetching…", async () =>
        {
            await Repo.FetchAsync(RemoteName);
            _lastFetched = DateTimeOffset.Now;
        });
        await LoadHostingInfoAsync();
    }

    // ── Open elsewhere ───────────────────────────────────────────────────────

    async Task OpenOnHostAsync(Func<Core.Hosting.HostedRemote, string?> link)
    {
        if (await RequireRemoteAsync()) await OpenLinkAsync(link(Remote!));
    }

    [RelayCommand] Task ViewOnHost() => OpenOnHostAsync(r => r.ProjectLink);
    [RelayCommand] Task CreateIssue() => OpenOnHostAsync(r => r.NewIssueLink);
    [RelayCommand] Task ViewIssues() => OpenOnHostAsync(r => r.IssuesLink);
    [RelayCommand] Task ViewChangeRequests() => OpenOnHostAsync(r => r.ChangeRequestsLink);
    [RelayCommand] Task ViewCiList() => OpenOnHostAsync(r => r.CiLink(Status?.Branch ?? DefaultBranch));

    [RelayCommand]
    Task OpenInTerminal() => Repo is null ? Task.CompletedTask : TryPlatform(() => _platform.OpenTerminal(Repo.Path));

    [RelayCommand]
    Task ShowInExplorer() => Repo is null ? Task.CompletedTask : TryPlatform(() => _platform.ShowInFileManager(Repo.Path));

    [RelayCommand]
    Task OpenInEditor() => Repo is null ? Task.CompletedTask : TryPlatform(() => _platform.OpenInEditor(Repo.Path));

    [RelayCommand]
    async Task RepositorySettings()
    {
        if (Repo is null) return;
        var current = await Repo.GetRemoteUrlAsync(RemoteName);
        var url = await _dialogs.PromptAsync("Repository settings", $"URL of the '{RemoteName}' remote:", current, "Save",
            "https://gitlab.example.com/group/project.git");
        if (string.IsNullOrWhiteSpace(url) || url == current) return;
        await RunAsync("Saving…", async () =>
        {
            await Repo.SetRemoteUrlAsync(RemoteName, url.Trim());
            await LoadRemoteAsync();
        });
    }

    // ── App ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    async Task Options()
    {
        var vm = _services.GetRequiredService<SettingsViewModel>();
        await _dialogs.PushModalAsync(new SettingsPage(vm));
        if (!await vm.Result) return;
        ApplySettings();
        if (_settings.AutoAddRepositories) await ScanRepositoriesFolderAsync();
    }

    [RelayCommand]
    async Task ScanRepositoriesFolder()
    {
        var added = await ScanRepositoriesFolderAsync();
        await _dialogs.AlertAsync("Repositories folder",
            added == 0
                ? $"No new repositories found in {_settings.RepositoriesDirectory}."
                : $"Added {added} repositor{(added == 1 ? "y" : "ies")} from {_settings.RepositoriesDirectory}.");
    }

    [RelayCommand]
    Task ShowCommandLog()
    {
        string text;
        lock (CommandLog) text = string.Join("\n\n", CommandLog.TakeLast(100));
        return _dialogs.PushModalAsync(new TextPage("Git command log", string.IsNullOrEmpty(text) ? "(no commands yet)" : text));
    }

    [RelayCommand]
    void Exit() => Application.Current?.Quit();
}
