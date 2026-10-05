using GitLabDesktop.Core.Git;

namespace GitLabDesktop.Core.Tests;

/// <summary>Runs alone: it points git's global config at a temporary file for the whole test process.</summary>
[CollectionDefinition(nameof(GlobalGitConfig), DisableParallelization = true)]
public class GlobalGitConfig;

[Collection(nameof(GlobalGitConfig))]
public class SubmoduleTests
{
    [Fact]
    public async Task GlobalBooleanSettingsRoundTripWithoutTouchingTheUsersConfig()
    {
        var file = Path.Combine(Path.GetTempPath(), "gld-gitconfig-" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllText(file, "");
        var previous = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", file);   // inherited by the git processes below
        try
        {
            var git = new GitRunner();
            Assert.Null(await GitRepository.GetGlobalBoolAsync(git, "submodule.recurse"));

            await GitRepository.SetGlobalBoolAsync(git, "submodule.recurse", true);
            Assert.True(await GitRepository.GetGlobalBoolAsync(git, "submodule.recurse"));
            Assert.Contains("recurse = true", File.ReadAllText(file));

            await GitRepository.SetGlobalBoolAsync(git, "submodule.recurse", false);
            Assert.Null(await GitRepository.GetGlobalBoolAsync(git, "submodule.recurse"));
            await GitRepository.SetGlobalBoolAsync(git, "submodule.recurse", false);   // already unset: no error
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", previous);
            File.Delete(file);
        }
    }

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
