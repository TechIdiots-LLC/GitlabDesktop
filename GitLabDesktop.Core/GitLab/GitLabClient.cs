using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitLabDesktop.Core.Hosting;

namespace GitLabDesktop.Core.GitLab;

public sealed class GitLabApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Minimal GitLab REST API v4 client authenticated with a personal access token.</summary>
public sealed class GitLabClient(HttpClient http) : IHostingService
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string? BaseUrl { get; private set; }
    string? _token;

    public bool IsConfigured => !string.IsNullOrEmpty(BaseUrl) && !string.IsNullOrEmpty(_token);

    public void Configure(string? baseUrl, string? token)
    {
        BaseUrl = baseUrl?.Trim().TrimEnd('/');
        _token = token?.Trim();
    }

    static string Id(string projectPath) => Uri.EscapeDataString(projectPath);

    HttpRequestMessage Request(HttpMethod method, string relative)
    {
        if (!IsConfigured) throw new InvalidOperationException("GitLab is not configured. Set the instance URL and access token in Options.");
        var req = new HttpRequestMessage(method, $"{BaseUrl}/api/v4/{relative}");
        req.Headers.Add("PRIVATE-TOKEN", _token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    async Task<T> SendAsync<T>(HttpRequestMessage req, CancellationToken ct)
    {
        using (req)
        using (var resp = await http.SendAsync(req, ct))
        {
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                throw new GitLabApiException(resp.StatusCode, $"GitLab API {(int)resp.StatusCode} {resp.ReasonPhrase}: {ExtractMessage(body)}");
            }
            return (await resp.Content.ReadFromJsonAsync<T>(Json, ct))!;
        }
    }

    static string ExtractMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var key in new[] { "message", "error_description", "error" })
                if (doc.RootElement.TryGetProperty(key, out var v))
                    return v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString();
        }
        catch (JsonException) { }
        return body.Length > 300 ? body[..300] : body;
    }

    Task<T> GetAsync<T>(string relative, CancellationToken ct = default)
        => SendAsync<T>(Request(HttpMethod.Get, relative), ct);

    public Task<GitLabUser> GetCurrentUserAsync(CancellationToken ct = default)
        => GetAsync<GitLabUser>("user", ct);

    public Task<List<GitLabProject>> GetProjectsAsync(string? search, int page = 1, CancellationToken ct = default)
    {
        var q = $"projects?membership=true&simple=true&order_by=last_activity_at&per_page=100&page={page}";
        if (!string.IsNullOrWhiteSpace(search)) q += "&search=" + Uri.EscapeDataString(search);
        return GetAsync<List<GitLabProject>>(q, ct);
    }

    public Task<GitLabProject> GetProjectAsync(string projectPath, CancellationToken ct = default)
        => GetAsync<GitLabProject>($"projects/{Id(projectPath)}", ct);

    public Task<List<GitLabBranch>> GetBranchesAsync(string projectPath, CancellationToken ct = default)
        => GetAsync<List<GitLabBranch>>($"projects/{Id(projectPath)}/repository/branches?per_page=100", ct);

    public async Task<GitLabMergeRequest?> GetOpenMergeRequestAsync(string projectPath, string sourceBranch, CancellationToken ct = default)
    {
        var list = await GetAsync<List<GitLabMergeRequest>>(
            $"projects/{Id(projectPath)}/merge_requests?state=opened&source_branch={Uri.EscapeDataString(sourceBranch)}", ct);
        return list.FirstOrDefault();
    }

    public async Task<GitLabPipeline?> GetLatestPipelineAsync(string projectPath, string refName, CancellationToken ct = default)
    {
        var list = await GetAsync<List<GitLabPipeline>>(
            $"projects/{Id(projectPath)}/pipelines?per_page=1&ref={Uri.EscapeDataString(refName)}", ct);
        return list.FirstOrDefault();
    }

    public Task<GitLabMergeRequest> CreateMergeRequestAsync(string projectPath, CreateMergeRequestRequest body, CancellationToken ct = default)
    {
        var req = Request(HttpMethod.Post, $"projects/{Id(projectPath)}/merge_requests");
        req.Content = JsonContent.Create(body, options: Json);
        return SendAsync<GitLabMergeRequest>(req, ct);
    }

    // ── IHostingService ──────────────────────────────────────────────────────

    public HostingKind Kind => HostingKind.GitLab;

    public bool Handles(HostedRemote remote) => remote.Kind == HostingKind.GitLab && remote.IsOn(BaseUrl);

    public async Task<HostedUser> GetUserAsync(CancellationToken ct = default)
    {
        var u = await GetCurrentUserAsync(ct);
        return new HostedUser(u.Username, u.Name);
    }

    public async Task<IReadOnlyList<HostedProject>> ListProjectsAsync(string? search, CancellationToken ct = default)
        => (await GetProjectsAsync(search, ct: ct))
            .Select(p => new HostedProject(p.PathWithNamespace, p.Description, p.HttpUrlToRepo, p.SshUrlToRepo, p.WebUrl))
            .ToList();

    public async Task<string?> GetDefaultBranchAsync(string projectPath, CancellationToken ct = default)
        => (await GetProjectAsync(projectPath, ct)).DefaultBranch;

    public async Task<IReadOnlyList<string>> ListBranchesAsync(string projectPath, CancellationToken ct = default)
        => (await GetBranchesAsync(projectPath, ct))
            .OrderByDescending(b => b.Default).ThenBy(b => b.Name, StringComparer.Ordinal)
            .Select(b => b.Name)
            .ToList();

    public async Task<ChangeRequest?> FindOpenChangeRequestAsync(string projectPath, string sourceBranch, CancellationToken ct = default)
        => await GetOpenMergeRequestAsync(projectPath, sourceBranch, ct) is { } mr
            ? new ChangeRequest(HostingKind.GitLab, mr.Iid, mr.Title, mr.WebUrl, mr.Draft)
            : null;

    public async Task<CiStatus?> GetCiStatusAsync(string projectPath, string branch, CancellationToken ct = default)
        => await GetLatestPipelineAsync(projectPath, branch, ct) is { } p ? new CiStatus(p.Status, p.WebUrl) : null;

    public async Task<ChangeRequest> CreateChangeRequestAsync(string projectPath, NewChangeRequest request, CancellationToken ct = default)
    {
        var title = request.Title.Trim();
        // GitLab marks drafts with a title prefix.
        if (request.Draft && !title.StartsWith("Draft:", StringComparison.OrdinalIgnoreCase)) title = "Draft: " + title;
        long? assignee = request.AssignToMe ? (await GetCurrentUserAsync(ct)).Id : null;

        var mr = await CreateMergeRequestAsync(projectPath, new CreateMergeRequestRequest
        {
            SourceBranch = request.SourceBranch,
            TargetBranch = request.TargetBranch,
            Title = title,
            Description = request.Description,
            AssigneeId = assignee,
            RemoveSourceBranch = request.RemoveSourceBranch,
            Squash = request.Squash,
        }, ct);
        return new ChangeRequest(HostingKind.GitLab, mr.Iid, mr.Title, mr.WebUrl, mr.Draft);
    }
}
