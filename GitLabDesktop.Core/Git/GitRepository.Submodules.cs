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
        await RemoveBrokenSubmoduleStubsAsync();
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
    /// A recursive checkout that failed on a new submodule leaves stubs that break git in that repository ("fatal: not a
    /// git repository: vendor/x/../../.git/modules/vendor/x", even for <c>git status</c>): .git/modules/&lt;name&gt;
    /// holding only a config file, and a working folder &lt;name&gt; holding only a .git file pointing at it. Neither
    /// has any data, so both are removed; the submodule is cloned afresh when a branch needs it. A real submodule's
    /// folder in .git/modules always has HEAD and subfolders (objects, refs), so it is never touched.
    /// </summary>
    /// <returns>Whether anything was removed.</returns>
    public async Task<bool> RemoveBrokenSubmoduleStubsAsync()
    {
        var gitDir = (await Run("rev-parse", "--absolute-git-dir")).StdOut.Trim();
        var modules = System.IO.Path.Combine(gitDir, "modules");
        if (!Directory.Exists(modules)) return false;

        var removed = false;
        // Deepest first, so a group folder (".git/modules/vendor") is looked at after what's inside it
        foreach (var dir in Directory.EnumerateDirectories(modules, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length).ToList())
        {
            // A stub has some files (a config, maybe hooks) but no repository: no HEAD or objects, and no submodule
            // repository inside it either (which keeps group folders like ".git/modules/vendor" safe).
            if (!Directory.Exists(dir) ||
                File.Exists(System.IO.Path.Combine(dir, "HEAD")) ||
                Directory.Exists(System.IO.Path.Combine(dir, "objects")) ||
                Directory.EnumerateFiles(dir, "HEAD", SearchOption.AllDirectories).Any() ||
                !Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any())
                continue;

            var name = System.IO.Path.GetRelativePath(modules, dir).Replace('\\', '/');
            var workDir = System.IO.Path.Combine(Path, name);
            var gitFile = System.IO.Path.Combine(workDir, ".git");
            if (File.Exists(gitFile) &&
                Directory.EnumerateFileSystemEntries(workDir).Count() == 1 &&
                File.ReadAllText(gitFile).Replace('\\', '/').TrimEnd().EndsWith("modules/" + name, StringComparison.Ordinal))
                Directory.Delete(workDir, recursive: true);
            Directory.Delete(dir, recursive: true);
            removed = true;
        }
        return removed;
    }
}
