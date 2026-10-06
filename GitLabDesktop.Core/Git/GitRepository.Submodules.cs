namespace GitLabDesktop.Core.Git;

public sealed partial class GitRepository
{
    /// <summary>
    /// Runs a command that moves HEAD or the working tree (switch, merge, pull…) with submodule recursion off, then
    /// brings the submodules in line with <see cref="SyncSubmodulesAsync"/>. With submodule.recurse on, git's own
    /// recursive checkout fails on a branch that adds a submodule ("not a git repository: .git/modules/…") because it
    /// can't clone one, and leaves the working tree half switched.
    /// </summary>
    async Task<GitResult> RunMovingHeadAsync(IEnumerable<string> args, CancellationToken ct = default)
    {
        var result = await Run(["-c", "submodule.recurse=false", .. args], ct: ct);
        await SyncSubmodulesAsync(ct);
        return result;
    }

    Task<GitResult> RunMovingHeadAsync(params string[] args) => RunMovingHeadAsync((IEnumerable<string>)args);

    /// <summary>
    /// When the repository has submodules and git is set to recurse into them (submodule.recurse, Options › Include
    /// submodules), clones any that are new and checks each out at the commit the current branch records.
    /// </summary>
    public async Task SyncSubmodulesAsync(CancellationToken ct = default)
    {
        if (!File.Exists(System.IO.Path.Combine(Path, ".gitmodules"))) return;
        var recurse = await Run(["config", "--type=bool", "--get", "submodule.recurse"], throwOnError: false);
        if (recurse.Success && recurse.StdOut.Trim() == "true")
        {
            await RemoveBrokenSubmoduleStubsAsync();
            await Run(["submodule", "update", "--init", "--recursive"], ct: ct);
        }
    }

    /// <summary>
    /// A recursive checkout that failed on a new submodule leaves stubs that make every later
    /// <c>git submodule update</c> fail too ("BUG: submodule considered for cloning…"): .git/modules/&lt;name&gt; holding
    /// only a config file, and a working folder holding only a .git file pointing at it. Neither has any data (no HEAD,
    /// no objects, no files), so they are removed and the submodule is cloned afresh. Real submodules are never touched.
    /// </summary>
    async Task RemoveBrokenSubmoduleStubsAsync()
    {
        var gitDir = (await Run("rev-parse", "--absolute-git-dir")).StdOut.Trim();
        var entries = await Run(["config", "-f", ".gitmodules", "--get-regexp", @"^submodule\..*\.path$"], throwOnError: false);
        foreach (var line in entries.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // "submodule.<name>.path <path>"
            var space = line.IndexOf(' ');
            if (space < 0) continue;
            var name = line["submodule.".Length..(space - ".path".Length)];
            var path = line[(space + 1)..];

            var moduleDir = System.IO.Path.Combine(gitDir, "modules", name);
            bool IsStub(string dir) => Directory.Exists(dir) &&
                                       !File.Exists(System.IO.Path.Combine(dir, "HEAD")) &&
                                       !Directory.Exists(System.IO.Path.Combine(dir, "objects"));
            if (!IsStub(moduleDir)) continue;

            var workDir = System.IO.Path.Combine(Path, path);
            if (Directory.Exists(workDir) &&
                Directory.EnumerateFileSystemEntries(workDir).Select(System.IO.Path.GetFileName).SequenceEqual([".git"]) &&
                File.Exists(System.IO.Path.Combine(workDir, ".git")))
                Directory.Delete(workDir, recursive: true);
            Directory.Delete(moduleDir, recursive: true);
        }
    }
}
