using GitLabDesktop.Core.Git;

namespace GitLabDesktop.Core.Tests;

/// <summary>Runs alone: it points git's global config at a temporary file for the whole test process.</summary>
[CollectionDefinition(nameof(GlobalGitConfig), DisableParallelization = true)]
public class GlobalGitConfig;

[Collection(nameof(GlobalGitConfig))]
public class SubmoduleTests
{
    /// <summary>Points git's global and system config at temporary files (inherited by the git processes it runs).</summary>
    static async Task WithTemporaryConfigAsync(string systemConfig, Func<string, Task> test)
    {
        var global = Path.Combine(Path.GetTempPath(), "gld-global-" + Guid.NewGuid().ToString("N")[..8]);
        var system = Path.Combine(Path.GetTempPath(), "gld-system-" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllText(global, "");
        File.WriteAllText(system, systemConfig);
        var previous = (Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL"), Environment.GetEnvironmentVariable("GIT_CONFIG_SYSTEM"));
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", global);
        Environment.SetEnvironmentVariable("GIT_CONFIG_SYSTEM", system);
        try
        {
            await test(global);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", previous.Item1);
            Environment.SetEnvironmentVariable("GIT_CONFIG_SYSTEM", previous.Item2);
            File.Delete(global);
            File.Delete(system);
        }
    }

    [Fact]
    public Task DefaultSettingsRoundTripWithoutTouchingTheUsersConfig() => WithTemporaryConfigAsync("", async global =>
    {
        var git = new GitRunner();
        Assert.Null(await GitRepository.GetDefaultBoolAsync(git, "submodule.recurse"));

        await GitRepository.SetDefaultBoolAsync(git, "submodule.recurse", true);
        Assert.True(await GitRepository.GetDefaultBoolAsync(git, "submodule.recurse"));
        Assert.Contains("recurse = true", File.ReadAllText(global));

        await GitRepository.SetDefaultBoolAsync(git, "submodule.recurse", false);
        Assert.Null(await GitRepository.GetDefaultBoolAsync(git, "submodule.recurse"));
        Assert.DoesNotContain("recurse", File.ReadAllText(global));   // removed, not set to false
        await GitRepository.SetDefaultBoolAsync(git, "submodule.recurse", false);   // already unset: no error
    });

    [Fact]
    public Task TurningOffASettingTheSystemConfigTurnsOnWritesFalse()
        => WithTemporaryConfigAsync("[core]\n\tlongpaths = true\n", async global =>
        {
            // Git for Windows' installer sets core.longpaths in the system config
            var git = new GitRunner();
            Assert.True(await GitRepository.GetDefaultBoolAsync(git, "core.longpaths"));

            await GitRepository.SetDefaultBoolAsync(git, "core.longpaths", false);
            Assert.False(await GitRepository.GetDefaultBoolAsync(git, "core.longpaths"));
            Assert.Contains("longpaths = false", File.ReadAllText(global));

            await GitRepository.SetDefaultBoolAsync(git, "core.longpaths", true);
            Assert.True(await GitRepository.GetDefaultBoolAsync(git, "core.longpaths"));
        });

    [Fact]
    public async Task CloneWithoutSubmodulesLeavesThemOut()
    {
        using var lib = await TempRepo.CreateAsync();
        lib.Write("lib.txt", "library\n");
        await lib.CommitAllAsync("library");

        using var app = await TempRepo.CreateAsync();
        app.Write("app.txt", "app\n");
        await app.RunAsync("-c", "protocol.file.allow=always", "submodule", "add", lib.Dir, "lib");
        await app.CommitAllAsync("app with a submodule");

        var target = Path.Combine(Path.GetTempPath(), "gld-clone-" + Guid.NewGuid().ToString("N")[..8], "app");
        try
        {
            await GitRepository.CloneAsync(new GitRunner(), app.Dir, target, recurseSubmodules: false);
            Assert.True(File.Exists(Path.Combine(target, "app.txt")));
            Assert.True(Directory.Exists(Path.Combine(target, "lib")));                 // the submodule's folder…
            Assert.False(File.Exists(Path.Combine(target, "lib", "lib.txt")));         // …left empty
        }
        finally
        {
            try
            {
                var root = Path.GetDirectoryName(target)!;
                foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(root, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
