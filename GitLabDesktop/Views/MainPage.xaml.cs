using System.Reflection;
using GitLabDesktop.Core.Git;
using GitLabDesktop.Services;
using GitLabDesktop.ViewModels;

namespace GitLabDesktop.Views;

public partial class MainPage : ContentPage
{
    readonly MainViewModel _vm;

    readonly AppUpdater _updater;

    public MainPage(MainViewModel vm, AppUpdater updater)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _updater = updater;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsDiffExpanded)) ApplyDiffExpanded(vm.IsDiffExpanded);
        };
        Loaded += OnFirstLoaded;
    }

    // Offer a newer release at startup; quietly does nothing when offline or turned off in Options.
    async void OnFirstLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnFirstLoaded;
        await _updater.CheckOnStartupAsync();
    }

    async void OnCheckForUpdatesClicked(object? sender, EventArgs e) => await _updater.CheckAsync(interactive: true);

    // Widths of the list columns while the diff is expanded, to put back afterwards.
    GridLength _leftWidth, _historyFilesWidth;

    /// <summary>
    /// Collapses (or restores) the list columns. Hiding a pane leaves its column's width behind, so the columns
    /// themselves go to zero; the splitters keep whatever width the user dragged them to.
    /// </summary>
    void ApplyDiffExpanded(bool expanded)
    {
        var left = RepoGrid.ColumnDefinitions[0];
        var files = HistoryFilesGrid.ColumnDefinitions[0];
        if (expanded)
        {
            _leftWidth = left.Width;
            _historyFilesWidth = files.Width;
            left.Width = new GridLength(0);
            files.Width = new GridLength(0);
        }
        else
        {
            left.Width = _leftWidth;
            files.Width = _historyFilesWidth;
        }
        // A column width change alone doesn't re-run the grid's layout on Windows.
        ((IView)RepoGrid).InvalidateMeasure();
        ((IView)HistoryFilesGrid).InvalidateMeasure();
    }

    // Context menu items inherit the row's BindingContext.
    static T? Item<T>(object? sender) where T : class => (sender as Element)?.BindingContext as T;

    void OnFile(object? sender, Func<ChangedFileViewModel, Task> action)
    {
        if (Item<ChangedFileViewModel>(sender) is { } f) _ = action(f);
    }

    void OnCommit(object? sender, Func<CommitInfo, Task> action)
    {
        if (Item<CommitInfo>(sender) is { } c) _ = action(c);
    }

    // ── Changed file context menu ────────────────────────────────────────────
    void OnDiscardFile(object? s, EventArgs e) => OnFile(s, _vm.DiscardFileAsync);
    void OnIgnoreFile(object? s, EventArgs e) => OnFile(s, _vm.IgnoreFileAsync);
    void OnIgnoreExtension(object? s, EventArgs e) => OnFile(s, _vm.IgnoreExtensionAsync);
    void OnCopyFullPath(object? s, EventArgs e) => OnFile(s, f => _vm.CopyFilePathAsync(f, relative: false));
    void OnCopyRelativePath(object? s, EventArgs e) => OnFile(s, f => _vm.CopyFilePathAsync(f, relative: true));
    void OnShowFileInFolder(object? s, EventArgs e) => OnFile(s, _vm.ShowFileInFolderAsync);
    void OnOpenFileInEditor(object? s, EventArgs e) => OnFile(s, _vm.OpenFileInEditorAsync);
    void OnOpenFileDefault(object? s, EventArgs e) => OnFile(s, _vm.OpenFileWithDefaultAppAsync);

    // ── Commit context menu ──────────────────────────────────────────────────
    void OnAmendCommit(object? s, EventArgs e) => OnCommit(s, _vm.AmendCommitAsync);
    void OnResetToCommit(object? s, EventArgs e) => OnCommit(s, _vm.ResetToCommitAsync);
    void OnCheckoutCommit(object? s, EventArgs e) => OnCommit(s, _vm.CheckoutCommitAsync);
    void OnRevertCommit(object? s, EventArgs e) => OnCommit(s, _vm.RevertCommitAsync);
    void OnCreateBranchFromCommit(object? s, EventArgs e) => OnCommit(s, _vm.CreateBranchFromCommitAsync);
    void OnCreateTag(object? s, EventArgs e) => OnCommit(s, _vm.CreateTagAsync);
    void OnCherryPick(object? s, EventArgs e) => OnCommit(s, _vm.CherryPickCommitAsync);
    void OnCopySha(object? s, EventArgs e) => OnCommit(s, _vm.CopyShaAsync);
    void OnCopyTag(object? s, EventArgs e) => OnCommit(s, _vm.CopyTagAsync);
    void OnViewCommitOnHost(object? s, EventArgs e) => OnCommit(s, _vm.ViewCommitOnHostAsync);

    async void OnAboutClicked(object? sender, EventArgs e)
    {
        var version = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "DisplayVersion")?.Value ?? "?";
        await DisplayAlertAsync("GitLab Desktop", $"Version {version}\n\nA GitHub Desktop-style client for GitLab, built with .NET MAUI.", "OK");
    }
}
