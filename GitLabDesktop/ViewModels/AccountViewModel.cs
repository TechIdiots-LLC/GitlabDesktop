using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitLabDesktop.Core.Hosting;
using GitLabDesktop.Services;

namespace GitLabDesktop.ViewModels;

/// <summary>One editable account card in Options.</summary>
public sealed partial class AccountViewModel : ObservableObject
{
    readonly HostingRegistry _hosting;
    readonly Action<AccountViewModel> _remove;
    readonly string? _repository;
    readonly bool _remember;

    public AccountViewModel(HostAccount account, HostingRegistry hosting, Action<AccountViewModel> remove)
    {
        _hosting = hosting;
        _remove = remove;
        _repository = account.Repository;
        _remember = account.Remember;
        Kind = account.Kind;
        _baseUrl = account.BaseUrl;
        _userName = account.UserName ?? "";
        _secret = account.Secret ?? "";
        Title = account.Title;
    }

    public HostingKind Kind { get; }
    public bool IsGitLab => Kind == HostingKind.GitLab;
    public bool IsHosting => Kind is HostingKind.GitLab or HostingKind.GitHub;
    public string Title { get; }

    public string KindName => Kind switch
    {
        HostingKind.GitLab => "GitLab",
        HostingKind.GitHub => "GitHub",
        _ => "Git server",
    };

    /// <summary>Where the account applies, for accounts whose server URL is not edited here.</summary>
    public string ScopeText => (_repository is not null ? $"Only {Title}" : ToAccount().Host) + (_remember ? "" : " · until the app exits");
    public bool HasScopeText => !IsEditableServer;

    /// <summary>GitLab servers and github.com have a token page this app can open with the right scopes.</summary>
    public bool CanCreateToken => IsGitLab || (Kind == HostingKind.GitHub && HostAccount.SameHost(BaseUrl, AppSettings.GitHubUrl));

    /// <summary>A whole-server GitLab or other git server account, whose URL is typed in (github.com's is fixed; per-repository ones come from git).</summary>
    public bool IsEditableServer => (IsGitLab || Kind == HostingKind.Unknown) && _repository is null;

    public string ServerPlaceholder => IsGitLab ? "https://gitlab.example.com" : "https://git.example.com";

    // Other servers usually want a real username with a token; GitLab and GitHub accept any.
    public string UserNameLabel => IsHosting ? "Username (optional with an access token)" : "Username";

    /// <summary>Whole-server GitLab and github.com accounts are also the app's API sign-in.</summary>
    public bool UsesApi => ToAccount().UsesApi;

    public string SecretHelp => UsesApi
        ? (IsGitLab
            ? "Personal access token (scopes: api, read_user, write_repository), used for git and for merge requests and pipelines"
            : "Personal access token (classic, scopes: repo, workflow, read:user), used for git and for pull requests and checks")
        : "Password or access token, used for git over HTTPS";

    [ObservableProperty] private string _baseUrl;
    [ObservableProperty] private string _userName;
    [ObservableProperty] private string _secret;
    [ObservableProperty] private string? _testResult;
    [ObservableProperty] private bool _isTesting;

    public string NormalizedUrl
    {
        get
        {
            if (!IsEditableServer) return BaseUrl;
            var url = BaseUrl.Trim().TrimEnd('/');
            if (url.Length > 0 && !url.Contains("://")) url = "https://" + url;
            return url;
        }
    }

    public HostAccount ToAccount() => new()
    {
        Kind = Kind,
        BaseUrl = NormalizedUrl,
        Repository = _repository,
        UserName = string.IsNullOrWhiteSpace(UserName) ? null : UserName.Trim(),
        Secret = string.IsNullOrWhiteSpace(Secret) ? null : Secret.Trim(),
        Remember = _remember,
    };

    [RelayCommand]
    async Task Test()
    {
        IsTesting = true;
        TestResult = null;
        try
        {
            if (string.IsNullOrWhiteSpace(Secret) || (IsGitLab && string.IsNullOrWhiteSpace(BaseUrl)))
                throw new InvalidOperationException(IsGitLab ? "Enter the server URL and a personal access token." : "Enter a personal access token.");
            var user = await _hosting.Create(ToAccount()).GetUserAsync();
            TestResult = $"✓ Signed in as {user.Name} (@{user.Login})";
        }
        catch (Exception ex)
        {
            TestResult = $"✗ {ex.Message}";
        }
        IsTesting = false;
    }

    /// <summary>Opens the server's token page with a name and the scopes this app needs filled in.</summary>
    [RelayCommand]
    async Task CreateToken()
    {
        var url = IsGitLab
            ? $"{NormalizedUrl}/-/user_settings/personal_access_tokens?name=GitLab+Desktop&scopes=api,read_user,write_repository"
            : "https://github.com/settings/tokens/new?description=GitLab+Desktop&scopes=repo,workflow,read:user";
        if (!IsGitLab || NormalizedUrl.Length > 0) await Launcher.Default.OpenAsync(new Uri(url));
    }

    [RelayCommand]
    void Remove() => _remove(this);
}
