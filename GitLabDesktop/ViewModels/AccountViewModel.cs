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

    public AccountViewModel(HostAccount account, HostingRegistry hosting, Action<AccountViewModel> remove)
    {
        _hosting = hosting;
        _remove = remove;
        Kind = account.Kind;
        _baseUrl = account.BaseUrl;
        _token = account.Token ?? "";
        _useTokenForGit = account.UseTokenForGit;
    }

    public HostingKind Kind { get; }
    public bool IsGitLab => Kind == HostingKind.GitLab;
    public string KindName => IsGitLab ? "GitLab" : "GitHub";

    public string TokenHelp => IsGitLab
        ? "Personal access token (scopes: api, read_user, write_repository)"
        : "Personal access token (classic, scopes: repo, read:user), or a fine-grained token with pull request and contents access";

    [ObservableProperty] private string _baseUrl;
    [ObservableProperty] private string _token;
    [ObservableProperty] private bool _useTokenForGit;
    [ObservableProperty] private string? _testResult;
    [ObservableProperty] private bool _isTesting;

    public string NormalizedUrl
    {
        get
        {
            if (!IsGitLab) return AppSettings.GitHubUrl;
            var url = BaseUrl.Trim().TrimEnd('/');
            if (url.Length > 0 && !url.Contains("://")) url = "https://" + url;
            return url;
        }
    }

    public HostAccount ToAccount() => new()
    {
        Kind = Kind,
        BaseUrl = NormalizedUrl,
        Token = string.IsNullOrWhiteSpace(Token) ? null : Token.Trim(),
        UseTokenForGit = UseTokenForGit,
    };

    [RelayCommand]
    async Task Test()
    {
        IsTesting = true;
        TestResult = null;
        try
        {
            if (string.IsNullOrWhiteSpace(Token) || (IsGitLab && string.IsNullOrWhiteSpace(BaseUrl)))
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
            : "https://github.com/settings/tokens/new?description=GitLab+Desktop&scopes=repo,read:user";
        if (!IsGitLab || NormalizedUrl.Length > 0) await Launcher.Default.OpenAsync(new Uri(url));
    }

    [RelayCommand]
    void Remove() => _remove(this);
}
