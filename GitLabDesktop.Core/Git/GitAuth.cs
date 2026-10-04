namespace GitLabDesktop.Core.Git;

/// <summary>Recognising git authentication failures and the HTTPS server they came from.</summary>
public static class GitAuth
{
    // What git, Git Credential Manager, GitLab and GitHub print when a login is missing, wrong or not allowed.
    static readonly string[] Markers =
    [
        "Authentication failed",
        "could not read Username",
        "could not read Password",
        "terminal prompts disabled",
        "Cannot prompt because user interactivity has been disabled",
        "HTTP Basic: Access denied",
        "Invalid username or password",
        "Invalid username or token",
        "The requested URL returned error: 401",
        "The requested URL returned error: 403",
        "Write access to repository not granted",
        "You are not allowed to push code",
    ];

    public static bool IsAuthenticationFailure(string? gitOutput)
        => gitOutput is not null &&
           (Markers.Any(m => gitOutput.Contains(m, StringComparison.OrdinalIgnoreCase)) ||
            // GitHub: "remote: Permission to owner/repo.git denied to user."
            (gitOutput.Contains("Permission to ", StringComparison.Ordinal) && gitOutput.Contains(" denied to ", StringComparison.Ordinal)));

    /// <summary>
    /// "https://host[:port]" for an HTTP(S) remote, without any user name in the URL; null for SSH and local remotes,
    /// which a username/password cannot fix.
    /// </summary>
    public static string? HttpServer(string? remoteUrl)
    {
        if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        return uri.IsDefaultPort ? $"{uri.Scheme}://{uri.Host}" : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }

    /// <summary>The user name embedded in a remote URL (https://user@host/...), if any.</summary>
    public static string? UserName(string? remoteUrl)
    {
        if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo)) return null;
        var user = uri.UserInfo.Split(':')[0];
        return user.Length == 0 ? null : Uri.UnescapeDataString(user);
    }

    /// <summary>Whether a secret looks like a GitLab or GitHub access token rather than a password.</summary>
    public static bool LooksLikeToken(string? secret)
        => secret is not null &&
           (secret.StartsWith("glpat-", StringComparison.Ordinal) || secret.StartsWith("gloas-", StringComparison.Ordinal) ||
            secret.StartsWith("ghp_", StringComparison.Ordinal) || secret.StartsWith("github_pat_", StringComparison.Ordinal) ||
            secret.StartsWith("gho_", StringComparison.Ordinal));
}
