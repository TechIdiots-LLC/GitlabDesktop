using System.Reflection;
using GitLabDesktop.Core.Git;
using GitLabDesktop.ViewModels;

namespace GitLabDesktop.Views;

public partial class MainPage : ContentPage
{
    readonly MainViewModel _vm;

    public MainPage(MainViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
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
