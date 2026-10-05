using System.Diagnostics;
using System.Text;

namespace GitLabDesktop.Core.Git;

public sealed record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}

public sealed class GitException(string message, GitResult result) : Exception(message)
{
    public GitResult Result { get; } = result;
}

/// <summary>
/// Runs the git command-line client. Repository operations shell out to git (as GitHub Desktop
/// does) so behaviour, hooks, credential helpers and config all match what the user gets in a terminal.
/// </summary>
public sealed class GitRunner
{
    static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Path to git.exe, or "git" to resolve from PATH.</summary>
    public string GitExecutable { get; set; } = "git";

    /// <summary>
    /// Extra git config passed through GIT_CONFIG_COUNT/KEY/VALUE environment variables, so secrets
    /// such as an access-token auth header never appear on the process command line.
    /// </summary>
    public Func<IReadOnlyList<KeyValuePair<string, string>>>? ConfigProvider { get; set; }

    /// <summary>Raised after every command, for the output log.</summary>
    public event Action<string, GitResult>? CommandCompleted;

    public async Task<GitResult> RunAsync(
        string workingDirectory,
        IEnumerable<string> args,
        string? stdin = null,
        bool throwOnError = true,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(GitExecutable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (stdin is not null)
            psi.StandardInputEncoding = Utf8NoBom;

        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("color.ui=never");
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        // Never prompt for credentials, in a terminal or in Git Credential Manager's window: the app asks for a sign-in
        // itself when git reports an authentication failure. Credentials GCM already stored are still used.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "never";
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        var config = ConfigProvider?.Invoke();
        if (config is { Count: > 0 })
        {
            psi.Environment["GIT_CONFIG_COUNT"] = config.Count.ToString();
            for (int i = 0; i < config.Count; i++)
            {
                psi.Environment[$"GIT_CONFIG_KEY_{i}"] = config[i].Key;
                psi.Environment[$"GIT_CONFIG_VALUE_{i}"] = config[i].Value;
            }
        }

        using var proc = new Process { StartInfo = psi };
        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            throw new GitException($"Could not start git ({GitExecutable}): {ex.Message}. Is git installed?",
                new GitResult(-1, "", ex.Message));
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        if (stdin is not null)
        {
            await proc.StandardInput.WriteAsync(stdin.AsMemory(), ct);
            proc.StandardInput.Close();
        }

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        var result = new GitResult(proc.ExitCode, await stdoutTask, await stderrTask);
        var display = "git " + string.Join(' ', psi.ArgumentList.Skip(4));
        CommandCompleted?.Invoke(display, result);

        if (throwOnError && !result.Success)
        {
            var msg = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            throw new GitException(msg.Trim(), result);
        }
        return result;
    }
}
