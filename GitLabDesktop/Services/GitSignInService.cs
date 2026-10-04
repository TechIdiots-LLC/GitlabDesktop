using GitLabDesktop.Core.Git;
using GitLabDesktop.Core.Hosting;
using GitLabDesktop.ViewModels;
using GitLabDesktop.Views;

namespace GitLabDesktop.Services;

public enum SignInOutcome
{
    /// <summary>New credentials are in place; run the git command again.</summary>
    Retry,
    Cancelled,
    /// <summary>Not something a password can fix (SSH or local remote); report git's error instead.</summary>
    NotApplicable,
}

/// <summary>
/// Handles git authentication failures on HTTPS remotes: asks for a username and password or token, stores it for the
/// server or just the repository, and optionally makes a token the server's API token as well.
/// </summary>
public sealed class GitSignInService(AppSettings settings, HostingRegistry hosting, DialogService dialogs)
{
    public static bool IsAuthenticationFailure(Exception ex)
        => ex is GitException && GitAuth.IsAuthenticationFailure(ex.Message);

    public async Task<SignInOutcome> PromptAsync(string? remoteUrl, string gitError)
    {
        if (GitAuth.HttpServer(remoteUrl) is not { } server) return SignInOutcome.NotApplicable;

        // The repository URL git requests use, without user info, as the scope of a repository-only login.
        var uri = new Uri(remoteUrl!);
        var repositoryUrl = server + uri.AbsolutePath.TrimEnd('/');
        var remote = HostedRemote.Parse(remoteUrl, settings.KnownHosts);
        var existing = settings.FindGitLogin(server, repositoryUrl);
        var account = settings.Accounts.FirstOrDefault(a => remote?.IsOn(a.BaseUrl) == true);
        var failingAccount = existing is null && account is { UseTokenForGit: true, Token.Length: > 0 } ? account.Title : null;
        // A token typed here can also become the server's API token, for GitLab servers and github.com.
        bool canUseForApi = remote?.Kind == HostingKind.GitLab ||
                            (remote?.Kind == HostingKind.GitHub && remote.IsOn(AppSettings.GitHubUrl));

        var vm = new GitSignInViewModel(server, remote, remote?.ProjectPath, existing?.UserName ?? GitAuth.UserName(remoteUrl),
            existing is not null, failingAccount, canUseForApi, gitError);
        await dialogs.PushModalAsync(new GitSignInPage(vm));
        if (await vm.Result is not { } input) return SignInOutcome.Cancelled;

        await settings.SetGitLoginAsync(
            new AppSettings.GitLogin(server, input.UserName, input.Secret, input.ThisRepositoryOnly ? repositoryUrl : null),
            input.Remember);

        if (input.UseForApi && remote is not null)
            await UseAsApiTokenAsync(remote, input.Secret);
        return SignInOutcome.Retry;
    }

    /// <summary>Creates or updates the account for the remote's server with this token.</summary>
    async Task UseAsApiTokenAsync(HostedRemote remote, string token)
    {
        var accounts = settings.Accounts.Select(a => a.Clone()).ToList();
        var account = accounts.FirstOrDefault(a => remote.IsOn(a.BaseUrl));
        if (account is null)
        {
            var baseUrl = remote.Kind == HostingKind.GitHub
                ? AppSettings.GitHubUrl
                : remote.WebUrl[..^(remote.ProjectPath.Length + 1)];
            account = new HostAccount { Kind = remote.Kind, BaseUrl = baseUrl };
            accounts.Add(account);
        }
        account.Token = token;
        await settings.SaveAccountsAsync(accounts);
        hosting.Rebuild();
    }
}
