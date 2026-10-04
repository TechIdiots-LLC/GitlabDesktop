using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitLabDesktop.Core.Hosting;

namespace GitLabDesktop.Services;

/// <summary>An account on a GitLab server or on GitHub. The token is kept in SecureStorage, not in Preferences.</summary>
public sealed class HostAccount
{
    [JsonConverter(typeof(JsonStringEnumConverter<HostingKind>))]
    public HostingKind Kind { get; set; }

    /// <summary>Server URL, e.g. https://gitlab.example.com (GitHub accounts are always https://github.com).</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Send the token to git for HTTPS remotes on this server.</summary>
    public bool UseTokenForGit { get; set; }

    [JsonIgnore] public string? Token { get; set; }

    [JsonIgnore] public string Host => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var u) ? u.Host : BaseUrl;
    [JsonIgnore] public string Title => Kind == HostingKind.GitHub ? "GitHub" : $"GitLab ({Host})";

    public HostAccount Clone() => (HostAccount)MemberwiseClone();
}

/// <summary>User settings. Access tokens live in SecureStorage; everything else in Preferences.</summary>
public sealed class AppSettings
{
    public const string GitHubUrl = "https://github.com";

    /// <summary>Where repositories live: the default clone and "new repository" location, and the folder scanned for existing checkouts.</summary>
    public string RepositoriesDirectory
    {
        get => Preferences.Get("clone_directory", DefaultRepositoriesDirectory);
        set => Preferences.Set("clone_directory", value);
    }

    /// <summary>The app's own folder, Documents\GitLab, used until the user picks another.</summary>
    public static string DefaultRepositoriesDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GitLab");

    /// <summary>Add every repository found in <see cref="RepositoriesDirectory"/> to the repository list at startup.</summary>
    public bool AutoAddRepositories
    {
        get => Preferences.Get("auto_add_repositories", true);
        set => Preferences.Set("auto_add_repositories", value);
    }

    /// <summary>Repositories the user removed from the list, so the folder scan does not add them back.</summary>
    public List<string> HiddenRepositories
    {
        get
        {
            try { return JsonSerializer.Deserialize<List<string>>(Preferences.Get("hidden_repositories", "[]")) ?? []; }
            catch (JsonException) { return []; }
        }
        set => Preferences.Set("hidden_repositories", JsonSerializer.Serialize(value));
    }

    public bool CloneWithSsh
    {
        get => Preferences.Get("clone_with_ssh", false);
        set => Preferences.Set("clone_with_ssh", value);
    }

    public string EditorCommand
    {
        get => Preferences.Get("editor_command", "code");
        set => Preferences.Set("editor_command", value);
    }

    public string GitExecutable
    {
        get => Preferences.Get("git_executable", "git");
        set => Preferences.Set("git_executable", string.IsNullOrWhiteSpace(value) ? "git" : value.Trim());
    }

    public string? LastRepository
    {
        get => Preferences.Get("last_repository", null as string);
        set => Preferences.Set("last_repository", value);
    }

    public List<string> Repositories
    {
        get
        {
            try { return JsonSerializer.Deserialize<List<string>>(Preferences.Get("repositories", "[]")) ?? []; }
            catch (JsonException) { return []; }
        }
        set => Preferences.Set("repositories", JsonSerializer.Serialize(value));
    }

    // ── Accounts ─────────────────────────────────────────────────────────────

    // Cached because SecureStorage is async and git needs the tokens synchronously for every command.
    List<HostAccount> _accounts = [];
    bool _accountsLoaded;

    public IReadOnlyList<HostAccount> Accounts => _accounts;

    static string TokenKey(string baseUrl) => "token:" + baseUrl;

    public async Task LoadAccountsAsync()
    {
        if (_accountsLoaded) return;
        try { _accounts = JsonSerializer.Deserialize<List<HostAccount>>(Preferences.Get("accounts", "[]")) ?? []; }
        catch (JsonException) { _accounts = []; }

        foreach (var a in _accounts)
        {
            try { a.Token = await SecureStorage.Default.GetAsync(TokenKey(a.BaseUrl)); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AppSettings] SecureStorage read failed: {ex.Message}"); }
        }

        await MigrateSingleGitLabAccountAsync();
        await LoadGitLoginsAsync();
        _accountsLoaded = true;
    }

    /// <summary>Version 0.1 stored one GitLab URL and token; turn them into an account.</summary>
    async Task MigrateSingleGitLabAccountAsync()
    {
        var url = Preferences.Get("gitlab_url", "");
        if (string.IsNullOrEmpty(url)) return;
        string? token = null;
        try { token = await SecureStorage.Default.GetAsync("gitlab_token"); } catch { }
        if (!_accounts.Any(a => a.BaseUrl == url))
        {
            _accounts.Add(new HostAccount
            {
                Kind = HostingKind.GitLab,
                BaseUrl = url,
                Token = token,
                UseTokenForGit = Preferences.Get("use_token_for_git", true),
            });
            await SaveAccountsAsync(_accounts);
        }
        Preferences.Remove("gitlab_url");
        Preferences.Remove("use_token_for_git");
        SecureStorage.Default.Remove("gitlab_token");
    }

    public async Task SaveAccountsAsync(IEnumerable<HostAccount> accounts)
    {
        var list = accounts.Select(a => a.Clone()).ToList();
        foreach (var gone in _accounts.Where(o => !list.Any(n => n.BaseUrl == o.BaseUrl)))
            SecureStorage.Default.Remove(TokenKey(gone.BaseUrl));
        foreach (var a in list)
        {
            if (string.IsNullOrWhiteSpace(a.Token)) SecureStorage.Default.Remove(TokenKey(a.BaseUrl));
            else await SecureStorage.Default.SetAsync(TokenKey(a.BaseUrl), a.Token.Trim());
        }
        Preferences.Set("accounts", JsonSerializer.Serialize(list));
        _accounts = list;
    }

    // ── Host detection cache ─────────────────────────────────────────────────

    /// <summary>What <see cref="HostProbe"/> found for hosts without an account, so each host is probed once.</summary>
    public Dictionary<string, HostingKind> DetectedHosts
    {
        get
        {
            try { return JsonSerializer.Deserialize<Dictionary<string, HostingKind>>(Preferences.Get("detected_hosts", "{}")) ?? []; }
            catch (JsonException) { return []; }
        }
        set => Preferences.Set("detected_hosts", JsonSerializer.Serialize(value));
    }

    /// <summary>Hosts whose kind is known: accounts first (they win), then probe results.</summary>
    public IReadOnlyList<KnownHost> KnownHosts =>
    [
        .. _accounts.Select(a => new KnownHost(a.Kind, a.BaseUrl)),
        .. DetectedHosts.Where(d => d.Value != HostingKind.Unknown).Select(d => new KnownHost(d.Value, "https://" + d.Key)),
    ];

    // ── Git sign-ins ─────────────────────────────────────────────────────────

    /// <summary>
    /// A username and password or access token for git over HTTPS, for every repository on <see cref="Server"/>, or
    /// only for <see cref="Repository"/> (its remote URL) so repositories on one server can use different logins.
    /// </summary>
    public sealed record GitLogin(string Server, string UserName, string Secret, string? Repository = null)
    {
        /// <summary>The URL this login applies to, and its storage key.</summary>
        public string Scope => Repository ?? Server;

        public string Display => Repository is null
            ? $"{new Uri(Server).Host} (all repositories) — {UserName}"
            : $"{Repository[(Server.Length + 1)..]} on {new Uri(Server).Host} — {UserName}";
    }

    // Remembered logins (SecureStorage, listed in Preferences) and logins kept only until the app exits; keyed by Scope.
    readonly Dictionary<string, GitLogin> _savedLogins = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, GitLogin> _sessionLogins = new(StringComparer.OrdinalIgnoreCase);

    static string LoginKey(string scope) => "gitlogin:" + scope;

    public IReadOnlyCollection<GitLogin> SavedGitLogins => _savedLogins.Values;

    async Task LoadGitLoginsAsync()
    {
        List<string> scopes;
        try { scopes = JsonSerializer.Deserialize<List<string>>(Preferences.Get("git_login_scopes", "[]")) ?? []; }
        catch (JsonException) { scopes = []; }
        foreach (var scope in scopes)
        {
            try
            {
                if (await SecureStorage.Default.GetAsync(LoginKey(scope)) is { } json &&
                    JsonSerializer.Deserialize<GitLogin>(json) is { } login)
                    _savedLogins[scope] = login;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AppSettings] git login read failed: {ex.Message}"); }
        }
    }

    /// <summary>Uses a login for git; with <paramref name="remember"/>, also after restarts.</summary>
    public async Task SetGitLoginAsync(GitLogin login, bool remember)
    {
        _sessionLogins.Remove(login.Scope);
        _savedLogins.Remove(login.Scope);
        if (remember)
        {
            await SecureStorage.Default.SetAsync(LoginKey(login.Scope), JsonSerializer.Serialize(login));
            _savedLogins[login.Scope] = login;
        }
        else
        {
            SecureStorage.Default.Remove(LoginKey(login.Scope));
            _sessionLogins[login.Scope] = login;
        }
        SaveLoginScopes();
    }

    public void ForgetGitLogin(GitLogin login)
    {
        _savedLogins.Remove(login.Scope);
        _sessionLogins.Remove(login.Scope);
        SecureStorage.Default.Remove(LoginKey(login.Scope));
        SaveLoginScopes();
    }

    void SaveLoginScopes() => Preferences.Set("git_login_scopes", JsonSerializer.Serialize(_savedLogins.Keys.ToList()));

    /// <summary>The login git would use for a remote: one for that repository, else one for its whole server.</summary>
    public GitLogin? FindGitLogin(string server, string repository)
    {
        var all = _savedLogins.Values.Concat(_sessionLogins.Values).ToList();
        return all.FirstOrDefault(l => string.Equals(l.Repository, repository, StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(l => l.Repository is null && string.Equals(l.Server, server, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"https://host[:port]" without user info, the form git logins and extraHeader scopes are keyed by.</summary>
    static string? ServerOf(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"
            ? (u.IsDefaultPort ? $"{u.Scheme}://{u.Host}" : $"{u.Scheme}://{u.Host}:{u.Port}")
            : null;

    /// <summary>
    /// Git config that authenticates HTTPS remotes with an Authorization header scoped by http.&lt;url&gt;.extraHeader,
    /// so a secret is only ever sent to its own server (or repository). Per server, a login from the sign-in dialog
    /// wins over an account token, since the dialog is how a failing token gets replaced.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> GetGitConfig()
    {
        static string Header(string user, string secret)
            => "Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{secret}"));

        var servers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in _accounts)
        {
            if (a.UseTokenForGit && !string.IsNullOrEmpty(a.Token) && ServerOf(a.BaseUrl) is { } server)
                // Both hosts accept any username with a token as the password; these are the conventional ones.
                servers[server] = Header(a.Kind == HostingKind.GitHub ? "x-access-token" : "oauth2", a.Token);
        }
        var logins = _savedLogins.Values.Concat(_sessionLogins.Values).ToList();
        foreach (var l in logins.Where(l => l.Repository is null))
            servers[l.Server] = Header(l.UserName, l.Secret);

        // Repository logins first. For each URL git keeps the most specific match seen so far and skips later, less
        // specific ones, so a repository's header shuts out its server's header instead of both being sent. (Resetting
        // with an empty value isn't an option: Windows drops environment variables with empty values.)
        var config = logins
            .Where(l => l.Repository is not null)
            .Select(l => new KeyValuePair<string, string>($"http.{l.Repository}.extraHeader", Header(l.UserName, l.Secret)))
            .ToList();
        config.AddRange(servers.Select(s => new KeyValuePair<string, string>($"http.{s.Key}/.extraHeader", s.Value)));
        return config;
    }
}
