# Options

**File › Options…** (Ctrl+,). Changes apply when you click **Save**.

## Accounts

GitLab, GitHub and other git server sign-ins. See [Accounts and signing in](accounts.md).

## Repositories

| Setting | |
| --- | --- |
| **Repositories folder** | Where clones and new repositories go by default, and where existing repositories are found. **Browse…** to choose a folder. |
| **Use default** | `Documents\GitLab`. |
| **Use GitHub Desktop folder** | `Documents\GitHub`, so repositories you cloned with GitHub Desktop show up too. |
| **Automatically add repositories found in this folder** | Adds repositories in the folder, and one level of subfolders, to the repository list. The line above it says how many were found. |
| **Clone with SSH instead of HTTPS** | The default for the **SSH** box in the clone dialog. |
| **Include submodules** | The default for **Include submodules** in the clone dialog. It is git's own `submodule.recurse` setting (in your global git config), so switching branches and pulling also update submodules, in the app and in git on the command line. Off unless you've turned it on. |

## Updates

| Setting | |
| --- | --- |
| **Check for a new version when the app starts** | On by default. **Help › Check for updates…** always works. |
| **Include pre-releases** | Also offer test versions such as `0.3.0-rc.1`. |

The installed version is shown below. See [Updates](getting-started.md#updates).

## Tools

| Setting | |
| --- | --- |
| **External editor command** | The command **Open in editor** runs, given the folder or file path. The default is `code` (Visual Studio Code). Examples: `code`, `notepad++`, `subl`, or a full path to the program. |
| **Git executable** | `git` from your PATH by default, or a full path to `git.exe`. **Test git** shows the version found. |

## Where settings are kept

Settings live in the app's local data folder:

- **Windows (installer)**: `%LOCALAPPDATA%\GitLabDesktop\GitLabDesktop\Settings`
- **macOS**: the app's container in `~/Library`

Passwords and tokens are kept in the system's secure storage, never in a plain settings file. The window's size and
position, the divider widths and the last open repository are remembered too.
