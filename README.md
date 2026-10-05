# GitLab Desktop

A GitHub Desktop-style git client for self-hosted GitLab servers and GitHub, built with .NET MAUI.

## Features

- **Changes tab**: changed files with checkboxes, a diff for each file, and line-level selection. Click the gutter next to a line to include or exclude it, or click a hunk header to toggle the whole hunk. Then commit with a summary and description, or amend the last commit.
- **History tab**: commit list (tags, unpushed `↑` markers, infinite scroll), the files each commit changed, and their diffs. The right-click menu has amend, reset to commit, checkout, revert, create branch, create tag, cherry-pick, copy SHA/tag, and view in browser.
- **Toolbar**: current repository, current branch with its open merge/pull request (`!12` on GitLab, `#12` on GitHub) and CI status, and a Fetch / Pull / Push / Publish button that changes with the branch state.
- **Repository menu**: Push, Pull, Fetch, Remove, Open in Terminal, Show in Explorer, Open in editor, Repository settings (remote URL), and host pages: view the project, create/view issues, merge or pull requests, and pipelines or Actions.
- **Branch menu**: New, Rename, Delete (local and remote), Discard / Stash / Restore changes, Update from the default branch, Merge, Squash and merge, Rebase, Continue / Abort, Compare and View branch on the host, **Create merge request / pull request** (in-app, through the API), and View request / CI status.
- **Clone**: browse and search the projects of any of your accounts, or paste any URL, over HTTPS or SSH.
- **Repositories folder** (Options): existing checkouts in it are added to the repository list automatically. It defaults to `Documents\GitLab`, with a one-click option for GitHub Desktop's `Documents\GitHub`.
- Switching branches with uncommitted changes offers to leave them on the current branch (as a stash) or bring them along. Stashes left this way are offered back when you return to that branch.
- The app refreshes whenever its window regains focus.
- **Updates**: the app checks this project's GitLab releases (GitHub as a fallback) at startup and from Help › Check for updates. A copy installed with the setup program downloads the new installer, checks its signature when the app is signed, and updates in place (`setup.exe /S /UPDATE`), restarting afterwards. Portable zip copies, macOS and Android open the download instead.

## GitLab and GitHub

Each repository's remote decides which wording and links it gets. GitLab repos get merge requests, pipelines and `!` references; GitHub repos get pull requests, Actions/checks and `#` references. The server type is decided in this order:

1. **An account in Options** for that server.
2. **Asking the server**, once per host, with the answer cached. GitLab answers `/api/v4/…` with an `X-Gitlab-Meta` header even without a token, and GitHub Enterprise answers `/api/v3/meta`. No server names are built in except `github.com`.
3. **The hostname** (`github.com`, or a name containing "gitlab" or "github"), when the server can't be reached.

If none of these settle it, the menus use the host name, only "View on <host>" works, and the app suggests adding an account.

## Setup

1. Install [Git for Windows](https://git-scm.com/download/win). The app runs the git CLI, so your config and hooks apply. Credentials Git Credential Manager already stored are still used, but it never opens its own sign-in window: the app asks instead.
2. **File › Options › Accounts**: add each GitLab server and/or GitHub with a personal access token. **Create token…** opens the server's token page with the needed scopes already selected. An account signs git in to its server over HTTPS, through an `http.<server>.extraHeader` setting passed in the environment so the secret never appears on a command line, and on GitLab and GitHub it also shows merge/pull requests and CI status. When git needs a sign-in it doesn't have, the app asks for one and saves it as an account, for the whole server or just that repository. Passwords and tokens are stored in the OS secure storage. Without an account, the browser links still work.
3. **File › Clone repository…** or **Add local repository…**. You can also run `GitLabDesktop.exe <folder>` to open a repository directly.

## Layout

| Project | Contents |
| --- | --- |
| `GitLabDesktop.Core` | Pure .NET 10 library: git CLI wrapper (`Git/`), status, diff and log parsing, the partial-commit patch builder, host detection and links (`Hosting/`), and the GitLab and GitHub REST clients (`GitLab/`, `GitHub/`). |
| `GitLabDesktop.Core.Tests` | xUnit tests, mostly against real temporary git repositories. |
| `GitLabDesktop` | MAUI app: `ViewModels/MainViewModel.*.cs` is split by menu area, and `Views/MainPage.xaml` holds the main window. |

How line-level commits work, the same way GitHub Desktop does it: on commit, the index is reset to HEAD, fully included files are added with `git add`, and for each partially included file a patch with only the selected lines is applied with `git apply --cached`.

## Build

```sh
dotnet build GitLabDesktop/GitLabDesktop.csproj -f net10.0-windows10.0.19041.0
dotnet test GitLabDesktop.Core.Tests
```

## Platforms

| Platform | Status |
| --- | --- |
| Windows (x64, ARM64) | Primary target. Released as a zip and an NSIS installer. |
| macOS (Mac Catalyst, universal) | Works: macOS includes git once the Xcode Command Line Tools are installed (`xcode-select --install`). Released as a `.dmg`. Without a Developer ID certificate it is ad-hoc signed, so right-click › Open it the first time. |
| Android | Preview. It installs and runs, but phones have no git command line, so repository operations fail. It needs a different git back end, such as libgit2, or an API-only mode for reviewing merge/pull requests and CI. |
| iOS | Compiles only; not released. |

## CI and releases

The GitLab pipeline (`.gitlab-ci.yml`) and GitHub workflows (`.github/workflows/`) do the same job and share the scripts in `build/`:

- **CI** runs on merge/pull requests and pushes to `main`. It runs the Core tests, then builds the Windows app (x64, ARM64), a debug-signed Android APK and, on GitHub, an ad-hoc signed macOS `.dmg`, all kept as 7-day artifacts.
- **Release** runs on pushes to `main`. When no release exists yet for `<ApplicationDisplayVersion>` in `GitLabDesktop.csproj`, it runs the tests, then builds and publishes:
  - the Windows zips and installers
  - the macOS `.dmg`
  - the Android APK (only when a release keystore is configured)

  The release notes are that version's section of `CHANGELOG.md`. Versions with a `-` (e.g. `0.2.0-rc.1`) are published as pre-releases.
- **Bump version** is started by hand: GitLab › Build › Pipelines › New pipeline with `BUMP_VERSION` set, or GitHub › Actions › *Create bump version PR*. It bumps the version, moves the changelog's `## master` notes under the new version (adding the merged merge/pull requests since the last tag), and opens a merge/pull request. Merging it triggers the release.

The GitLab pipeline runs on a Windows shell runner tagged `windows` with the .NET 10 SDK and MAUI workloads, git, Python 3, NSIS, and (for signing) the Windows SDK. The macOS jobs run only when the CI/CD variable `MACOS_RUNNER` is `true` and a runner tagged `macos` is registered.

| Setting (GitLab CI/CD variable / GitHub secret) | Needed for |
| --- | --- |
| `CI_PUSH_TOKEN` (GitLab only) | Bump version: project access token, Maintainer, scopes `api` + `write_repository` |
| `SIG_PFX_B64` / GitHub `SIG_PFX`, `SIG_PFX_PASS` | Optional: Windows code signing; unsigned with a warning otherwise |
| `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD` | Optional: the Android APK in releases |
| `MACOS_SIGNING_IDENTITY`, plus on GitHub `MACOS_CERT_P12_BASE64`, `MACOS_CERT_PASSWORD` | Optional: Developer ID signing of the macOS app |
| `APPLE_ID`, `APPLE_TEAM_ID`, `APPLE_APP_PASSWORD` | Optional: notarizing the signed macOS app |

On GitLab, make the secrets Protected and Masked with environment scope `signing/main`. On GitHub, put them in the `release` environment.
