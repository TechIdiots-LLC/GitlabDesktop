using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitLabDesktop.Core.Git;
using GitLabDesktop.Core.Hosting;

namespace GitLabDesktop.ViewModels;

/// <summary>What the user entered in the git sign-in dialog. A null username means an access token without one.</summary>
public sealed record GitSignInInput(string? UserName, string Secret, bool Remember, bool ThisRepositoryOnly);

/// <summary>
/// Asks for a username and password or access token after git could not authenticate to an HTTPS remote.
/// Completes with the input, or null when cancelled.
/// </summary>
public sealed partial class GitSignInViewModel : ModalViewModel<GitSignInInput?>
{
    readonly HostedRemote? _remote;

    /// <param name="rejected">The title of the account git just used and the server rejected, if any.</param>
    /// <param name="serverUsesApi">Whether the server's account is also the app's API sign-in (GitLab, github.com).</param>
    public GitSignInViewModel(string server, HostedRemote? remote, string? repositoryName, string? currentUser,
        string? rejected, bool serverUsesApi, string gitError)
    {
        _remote = remote;
        Host = new Uri(server).Host;
        RepositoryName = repositoryName;
        GitError = gitError.Trim();
        ServerUsesApi = serverUsesApi;
        _userName = currentUser ?? "";

        Heading = rejected is not null ? $"Sign-in to {Host} failed" : $"Sign in to {Host}";
        Message = rejected is not null
            ? $"{Host} rejected the sign-in from your {rejected} account{(currentUser is null ? "" : $" ({currentUser})")}. " +
              "Enter a new password or access token, or cancel."
            : $"Git needs a username and a password or access token to reach {Host}.";
    }

    protected override GitSignInInput? CancelledResult => null;

    public string Host { get; }
    public string? RepositoryName { get; }
    public bool HasRepositoryName => RepositoryName is not null;
    public string Heading { get; }
    public string Message { get; }
    public string GitError { get; }
    public bool ServerUsesApi { get; }

    public string AllRepositoriesText => $"All repositories on {Host}";
    public string ThisRepositoryText => $"Only this repository ({RepositoryName})";

    /// <summary>Shown while the sign-in is for the whole server and that account is also the API sign-in.</summary>
    public bool ShowApiNote => ServerUsesApi && ForAllRepositories;

    public string ApiNote => _remote?.Kind == HostingKind.GitHub
        ? "This account also shows pull requests and checks status, which need an access token rather than a password."
        : "This account also shows merge requests and pipeline status, which need an access token rather than a password.";

    public string SecretHelp => _remote?.Kind switch
    {
        HostingKind.GitHub => "GitHub needs an access token here, not your account password (scopes: repo, workflow).",
        HostingKind.GitLab => "With an access token (scope write_repository) the username can be left blank.",
        _ => "A password, or an access token if the server requires one.",
    };

    public bool CanCreateToken => _remote?.Kind is HostingKind.GitLab or HostingKind.GitHub;

    [ObservableProperty] private string _userName;
    [ObservableProperty] private string _secret = "";
    [ObservableProperty] private bool _remember = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowApiNote))]
    private bool _forAllRepositories = true;

    [ObservableProperty] private bool _forThisRepository;

    partial void OnForAllRepositoriesChanged(bool value) { if (value) ForThisRepository = false; }
    partial void OnForThisRepositoryChanged(bool value) { if (value) ForAllRepositories = false; }

    [RelayCommand]
    async Task CreateToken()
    {
        if (_remote is null) return;
        var url = _remote.Kind == HostingKind.GitHub
            ? "https://github.com/settings/tokens/new?description=GitLab+Desktop&scopes=repo,workflow,read:user"
            : $"{_remote.WebUrl[..^(_remote.ProjectPath.Length + 1)]}/-/user_settings/personal_access_tokens" +
              "?name=GitLab+Desktop&scopes=api,read_user,write_repository";
        await Launcher.Default.OpenAsync(new Uri(url));
    }

    [RelayCommand]
    void SignIn()
    {
        var secret = Secret.Trim();
        if (secret.Length == 0)
        {
            Error = "Enter a password or access token.";
            return;
        }
        var user = UserName.Trim();
        if (user.Length == 0 && !GitAuth.LooksLikeToken(secret) && _remote?.Kind != HostingKind.GitHub)
        {
            Error = "Enter your username to sign in with a password.";
            return;
        }
        // Without a username, git sends the token with the host's conventional one (see HostAccount.GitUserName).
        Complete(new GitSignInInput(user.Length == 0 ? null : user, secret, Remember, ForThisRepository && HasRepositoryName));
    }

    [RelayCommand]
    void Close() => Cancel();
}
