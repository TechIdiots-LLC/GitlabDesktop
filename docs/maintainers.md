# Building and releasing

## Building from source

You need the .NET 10 SDK with the MAUI workload (`dotnet workload install maui`) and git.

```sh
dotnet build GitLabDesktop/GitLabDesktop.csproj -f net10.0-windows10.0.19041.0
dotnet test GitLabDesktop.Core.Tests
```

`GitLabDesktop.sln` opens the projects in Visual Studio. To run against a repository directly:

```sh
GitLabDesktop.exe C:\path\to\repository
```

## Project layout

| Project | Contents |
| --- | --- |
| `GitLabDesktop.Core` | Plain .NET 10 library: the git CLI wrapper (`Git/`), status, diff and log parsing, the partial-commit patch builder, image headers, host detection and links (`Hosting/`), the GitLab and GitHub REST clients (`GitLab/`, `GitHub/`), and the update feed (`Updates/`). |
| `GitLabDesktop.Core.Tests` | xUnit tests, mostly against real temporary git repositories. |
| `GitLabDesktop` | The MAUI app. `ViewModels/MainViewModel.*.cs` is split by menu area, and `Views/MainPage.xaml` holds the main window. |
| `build/` | Scripts shared by the GitLab and GitHub pipelines: installer, signing, macOS packaging, version bump. |
| `docs/` | This documentation. |

### How line-level commits work

Line-level commits work the same way as in GitHub Desktop. On commit, the index is reset to HEAD and fully included
files are added with `git add`. For each partially included file, a patch with only the selected lines is applied
with `git apply --cached`.

### How git signs in

An account's secret reaches git as an `http.<url>.extraHeader` setting, passed through `GIT_CONFIG_COUNT` environment
variables, so it never appears on a command line. Repository-scoped sign-ins are listed before server-wide ones so they
win. `GIT_TERMINAL_PROMPT=0` and `GCM_INTERACTIVE=never` stop git and Git Credential Manager from prompting. When git
reports an authentication failure, the app shows its own sign-in prompt and retries.

## Platforms

| Platform | Status |
| --- | --- |
| Windows (x64, ARM64) | Primary target. Released as zips and NSIS installers. |
| macOS (Mac Catalyst, universal) | Released as a `.dmg`. Without a Developer ID certificate it is ad-hoc signed. |
| Android | Preview. Installs and runs, but phones have no git command line. It would need a different git back end (such as libgit2) or an API-only mode for reviewing merge/pull requests and CI. |
| iOS | Compiles only; not released. |

## CI and releases

The GitLab pipeline (`.gitlab-ci.yml`) and the GitHub workflows (`.github/workflows/`) do the same jobs and share the
scripts in `build/`. The project lives on GitLab and is mirrored to GitHub, so both run.

- **CI** runs on merge/pull requests and pushes to `main`. It runs the Core tests, then builds the Windows app (x64,
  ARM64), a debug-signed Android APK and, on GitHub (or GitLab with a macOS runner), an ad-hoc signed macOS `.dmg`.
  The builds are kept as 7-day artifacts.
- **Release** runs on pushes to `main`. When no release exists yet for `<ApplicationDisplayVersion>` in
  `GitLabDesktop/GitLabDesktop.csproj`, it runs the tests, then builds and publishes:
  - the Windows zips and installers;
  - the macOS `.dmg`;
  - the Android APK, only when a release keystore is configured.

  The release notes are that version's section of `CHANGELOG.md`. Versions with a `-` (for example `0.3.0-rc.1`) are
  published as pre-releases.
- **Bump version** is started by hand: on GitLab, *Build › Pipelines › New pipeline* with `BUMP_VERSION` set; on
  GitHub, *Actions › Create bump version PR*. It bumps the version and moves the changelog's `## master` notes under
  the new version, adding the merge/pull requests merged since the last tag. Then it opens a merge/pull request, and
  merging that triggers the release.

### Changelog

Add a line under `## master` in `CHANGELOG.md`, in *Features and improvements* or *Bug fixes*, with every
user-visible change. The bump job turns that section into the next version's release notes.

### Runners

The GitLab pipeline runs on a Windows shell runner tagged `windows` with:

- the .NET 10 SDK and MAUI workloads;
- git, Python 3 and NSIS;
- for signing, the Windows SDK.

The macOS jobs run only when the CI/CD variable `MACOS_RUNNER` is `true` and a runner tagged `macos` is registered.

### Secrets

| GitLab CI/CD variable / GitHub secret | Needed for |
| --- | --- |
| `CI_PUSH_TOKEN` (GitLab only) | Bump version: a project access token, role Maintainer, scopes `api` and `write_repository`. |
| `SIG_PFX_B64` (GitHub: `SIG_PFX`), `SIG_PFX_PASS` | Optional: Windows code signing. Without them the builds are unsigned, with a warning. |
| `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD` | Optional: the Android APK in releases. `build/Test-AndroidKeystore.ps1` checks a keystore and its passwords. |
| `MACOS_SIGNING_IDENTITY`, plus on GitHub `MACOS_CERT_P12_BASE64`, `MACOS_CERT_PASSWORD` | Optional: Developer ID signing of the macOS app. |
| `APPLE_ID`, `APPLE_TEAM_ID`, `APPLE_APP_PASSWORD` | Optional: notarizing the signed macOS app. |

On GitLab, make the secrets Protected and Masked with environment scope `signing/main`. On GitHub, put them in the
`release` environment.

### Signing and updates

Installed Windows copies update themselves by running the new installer with `/S /UPDATE`. Before running it, the app
checks the installer's Authenticode signature, but only when the running app is itself signed. A self-signed
certificate is accepted only if the installer is signed with exactly the same certificate (matching thumbprint) as the
running app. If releases are self-signed, changing the certificate means users must install the next version by hand
once.

The app finds releases on this project's GitLab releases first, and GitHub's as a fallback. The project locations are
constants in `GitLabDesktop/Services/AppUpdater.cs`, along with the documentation link used by Help › Documentation.

## Documentation

These pages are plain Markdown with relative links and images, so they read the same on GitLab and GitHub. Screenshots
in `docs/images/` are full window captures at 1728×918, taken from a demo repository with a clean profile so no real
accounts or paths appear. Keep new screenshots the same size, and don't show real server names or tokens.
