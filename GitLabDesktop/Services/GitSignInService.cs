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
/// Handles git authentication failures on HTTPS remotes: asks for a username and password or token, and stores it as
/// the account for the server or just the repository. A server's account on GitLab or github.com is also the app's
/// API sign-in there.
/// </summary>
public sealed class GitSignInService(AppSettings settings, HostingRegistry hosting, DialogService dialogs)
{
    public static bool IsAuthenticationFailure(Exception ex)
        => ex is GitException && GitAuth.IsAuthenticationFailure(ex.Message);

    public async Task<SignInOutcome> PromptAsync(string? remoteUrl, string gitError)
    {
        if (GitAuth.HttpServer(remoteUrl) is not { } server) return SignInOutcome.NotApplicable;

        // The repository URL git requests use, without user info, as the scope of a repository-only account.
        var uri = new Uri(remoteUrl!);
        var repositoryUrl = server + uri.AbsolutePath.TrimEnd('/');
        var remote = HostedRemote.Parse(remoteUrl, settings.KnownHosts);
        var existing = settings.FindAccount(server, repositoryUrl);
        var serverAccount = settings.FindAccount(server);

        var vm = new GitSignInViewModel(server, remote, remote?.ProjectPath,
            existing?.UserName ?? GitAuth.UserName(remoteUrl),
            rejected: existing is { Secret.Length: > 0 } ? existing.Title : null,
            serverUsesApi: serverAccount?.UsesApi ?? IsApiServer(remote),
            gitError);
        await dialogs.PushModalAsync(new GitSignInPage(vm));
        if (await vm.Result is not { } input) return SignInOutcome.Cancelled;

        var accounts = settings.Accounts.Select(a => a.Clone()).ToList();
        var account = input.ThisRepositoryOnly
            ? accounts.FirstOrDefault(a => string.Equals(a.Repository, repositoryUrl, StringComparison.OrdinalIgnoreCase))
            : accounts.FirstOrDefault(a => a.Repository is null && HostAccount.SameHost(a.BaseUrl, server));
        if (account is null)
        {
            account = new HostAccount
            {
                Kind = remote?.Kind ?? HostingKind.Unknown,
                BaseUrl = ServerBaseUrl(server, remote),
                Repository = input.ThisRepositoryOnly ? repositoryUrl : null,
            };
            accounts.Add(account);
        }
        account.UserName = input.UserName;
        account.Secret = input.Secret;
        account.Remember = input.Remember;
        await settings.SaveAccountsAsync(accounts);
        hosting.Rebuild();
        return SignInOutcome.Retry;
    }

    static bool IsApiServer(HostedRemote? remote)
        => remote?.Kind == HostingKind.GitLab || (remote?.Kind == HostingKind.GitHub && remote.IsOn(AppSettings.GitHubUrl));

    /// <summary>The account URL for a server: github.com, a GitLab server's web root (which may have a path), or the server.</summary>
    static string ServerBaseUrl(string server, HostedRemote? remote) => remote?.Kind switch
    {
        HostingKind.GitHub when remote.IsOn(AppSettings.GitHubUrl) => AppSettings.GitHubUrl,
        HostingKind.GitLab => remote.WebUrl[..^(remote.ProjectPath.Length + 1)],
        _ => server,
    };
}
