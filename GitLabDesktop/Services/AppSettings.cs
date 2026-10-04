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

    /// <summary>
    /// Git config that authenticates HTTPS remotes with each account's token, for accounts that opted in.
    /// Scoped with http.&lt;url&gt;.extraHeader so a token is only ever sent to its own server.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> GetGitConfig()
    {
        var config = new List<KeyValuePair<string, string>>();
        foreach (var a in _accounts)
        {
            if (!a.UseTokenForGit || string.IsNullOrEmpty(a.Token) || !Uri.TryCreate(a.BaseUrl, UriKind.Absolute, out var url))
                continue;
            // Both hosts accept any username with a token as the password; these are the conventional ones.
            var user = a.Kind == HostingKind.GitHub ? "x-access-token" : "oauth2";
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{a.Token}"));
            config.Add(new($"http.{url.GetLeftPart(UriPartial.Authority)}/.extraHeader", $"Authorization: Basic {basic}"));
        }
        return config;
    }
}
