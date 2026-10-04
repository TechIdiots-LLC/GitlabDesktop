using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitLabDesktop.Core.Git;
using GitLabDesktop.Core.Hosting;

namespace GitLabDesktop.ViewModels;

/// <summary>What the user entered in the git sign-in dialog.</summary>
public sealed record GitSignInInput(string UserName, string Secret, bool Remember, bool ThisRepositoryOnly, bool UseForApi);

/// <summary>
/// Asks for a username and password or access token after git could not authenticate to an HTTPS remote.
/// Completes with the input, or null when cancelled.
/// </summary>
public sealed partial class GitSignInViewModel : ModalViewModel<GitSignInInput?>
{
    readonly HostedRemote? _remote;
    bool _useForApiTouched;

    public GitSignInViewModel(string server, HostedRemote? remote, string? repositoryName, string? currentUser,
        bool hadLogin, string? failingAccountTitle, bool canUseForApi, string gitError)
    {
        _remote = remote;
        Host = new Uri(server).Host;
        RepositoryName = repositoryName;
        GitError = gitError.Trim();
        CanUseForApi = canUseForApi;
        _userName = currentUser ?? "";

        Heading = hadLogin || failingAccountTitle is not null ? $"Sign-in to {Host} failed" : $"Sign in to {Host}";
        Message = (hadLogin, failingAccountTitle) switch
        {
            (true, _) => $"{Host} rejected the saved sign-in{(currentUser is null ? "" : $" for {currentUser}")}. " +
                         "Enter a new password or access token, or cancel.",
            (_, { } title) => $"{Host} rejected the access token from your {title} account. " +
                              "Enter another password or access token, or cancel.",
            _ => $"Git needs a username and a password or access token to reach {Host}.",
        };
    }

    protected override GitSignInInput? CancelledResult => null;

    public string Host { get; }
    public string? RepositoryName { get; }
    public bool HasRepositoryName => RepositoryName is not null;
    public string Heading { get; }
    public string Message { get; }
    public string GitError { get; }
    public bool CanUseForApi { get; }

    public string AllRepositoriesText => $"All repositories on {Host}";
    public string ThisRepositoryText => $"Only this repository ({RepositoryName})";

    public string UseForApiText => _remote?.Kind == HostingKind.GitHub
        ? "Also use this token for pull requests and checks status"
        : "Also use this token for merge requests and pipeline status";

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
    [ObservableProperty] private bool _forAllRepositories = true;
    [ObservableProperty] private bool _forThisRepository;
    [ObservableProperty] private bool _useForApi;

    partial void OnForAllRepositoriesChanged(bool value) { if (value) ForThisRepository = false; }
    partial void OnForThisRepositoryChanged(bool value) { if (value) ForAllRepositories = false; }

    // Suggest API use for a token, until the user decides for themselves.
    partial void OnSecretChanged(string value)
    {
        if (!_useForApiTouched) SetUseForApi(CanUseForApi && GitAuth.LooksLikeToken(value));
    }

    bool _settingUseForApi;
    void SetUseForApi(bool value)
    {
        _settingUseForApi = true;
        UseForApi = value;
        _settingUseForApi = false;
    }

    partial void OnUseForApiChanged(bool value) { if (!_settingUseForApi) _useForApiTouched = true; }

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
        if (user.Length == 0)
        {
            if (!GitAuth.LooksLikeToken(secret) && _remote?.Kind != HostingKind.GitHub)
            {
                Error = "Enter your username to sign in with a password.";
                return;
            }
            // Both hosts accept any username with a token; these are the conventional ones.
            user = _remote?.Kind == HostingKind.GitHub ? "x-access-token" : "oauth2";
        }
        Complete(new GitSignInInput(user, secret, Remember, ForThisRepository && HasRepositoryName, CanUseForApi && UseForApi));
    }

    [RelayCommand]
    void Close() => Cancel();
}
