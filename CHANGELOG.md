# Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._

### 🐞 Bug fixes
- _...Add new stuff here..._

## 0.4.1
### ✨ Features and improvements
- Installing an update on Windows now shows an "Updating GitLab Desktop" progress window instead of running silently, then starts the app again. The installed version starts the installer, so this shows from the update after this one.

### 🐞 Bug fixes

## 0.4.0
### ✨ Features and improvements
- **Resolve conflicts in the app**, like GitHub Desktop: when a merge, rebase, pull, cherry-pick or revert stops on conflicts, a dialog lists every conflicted file with how many conflicts are left, with **Open in editor** and **Use a version** (either branch's file, or keeping a deletion). It watches the files, so a file saved without conflict markers shows as resolved straight away, with nothing to mark; **Continue** is enabled once all are. A banner and the Changes tab offer the same choices afterwards.
- A push refused because the server has newer commits now explains that and offers **Fetch**, after which the toolbar offers Pull with both counts, like GitHub Desktop.
- A pull blocked by uncommitted changes offers **Stash and pull**: the changes are set aside, the pull runs, and they are put back on top (any that clash are marked as conflicted, and a copy stays in the stash).

### 🐞 Bug fixes

## 0.3.1
### ✨ Features and improvements

### 🐞 Bug fixes
- After an update, the taskbar and Start menu could keep showing the old icon, because Windows had cached it. The installer now tells Explorer to refresh icons.

## 0.3.0
### ✨ Features and improvements
- **Image previews**: changed images (PNG, JPEG, GIF, BMP, ICO, WebP, TIFF) are shown as pictures instead of "Binary file changed", in Changes and History: the old one framed in red and the new one in green side by side, or a single "Added"/"Deleted" image, on a checkerboard so transparency shows, with format, pixel size and file size. Icons show their largest image.
- The repository and branch lists drop down under their toolbar buttons, like GitHub Desktop, instead of filling the window. Add/Clone and New branch are buttons beside the filter, branches are grouped into the default branch, other branches and remote branches, and Enter picks the first match while Esc or a click outside closes the list.
- **Git server accounts**: Options › Accounts › + Add Git server account signs git in to any other server over HTTPS (Gitea, Bitbucket Server, a plain git host and so on) with a username and password or token. These accounts are used for git only; merge/pull requests and CI need a GitLab or GitHub account.
- Help › Documentation (F1) opens the user guide.
- **Server type**: when the app can't tell whether a server runs GitLab or GitHub Enterprise, the sign-in prompt asks. A GitLab server behind a proxy can then still get merge requests, pipelines and **Create token…**, and GitHub Enterprise gets GitHub's wording and links. Every account in Options except github.com can have its type changed, and GitHub Enterprise accounts now have an editable server URL.
- New GitLab Desktop logo: the app, installer, taskbar and Start menu icons (sharp from 16 to 256 px on Windows), the macOS icon, the Android/iOS launcher icon and splash screen, and the welcome screen.

### 🐞 Bug fixes
- The "Use this sign-in for" choices in the sign-in prompt showed no text on Windows.
- The sign-in prompt now checks what the server runs before asking, instead of treating a server the app hadn't checked yet as a plain git server. A server that gives no answer isn't asked again for 10 minutes.
- Installing an update no longer fails with "The downloaded installer isn't validly signed" when releases are signed with a self-signed certificate. The installer must still be untampered and signed with exactly the certificate the running app is signed with.

## 0.2.0
### ✨ Features and improvements
- Accounts and saved git sign-ins are now one list of accounts. An account signs git in to its server (or one repository), and on GitLab and GitHub it is also used for merge/pull requests and CI status; signing in when git asks adds or updates the account. Existing sign-ins are converted automatically. An account's token is now always used for git on its server.
- The Changes tab shows how many files have changed.
- Right-click the changed files header to discard or stash all changes.

### 🐞 Bug fixes
- Syncing a repository without a saved sign-in no longer shows two sign-in prompts: Git Credential Manager no longer opens its own window, and the app asks once.

## 0.1.2
### ✨ Features and improvements

### 🐞 Bug fixes
- Dragging the dividers between the lists and the diff now resizes them on Windows (the cursor changed, but the layout didn't update), and a divider's width is only saved after an actual drag.
- Commits in History no longer show as "Not yet pushed" after pushing them.

## 0.1.1
### ✨ Features and improvements
- The window remembers its size, position and maximized state between runs. The first run, or a saved position that is no longer on any screen, opens centred and sized to fit the display.
- Drag the dividers between the file/commit lists and the diff to resize them; the widths are remembered.
- **Full width** button on the diff (also View › Full-width diff, Ctrl+3) hides the lists so the diff uses the whole window. It always starts off after a restart.
- **Updates**: the app checks its GitLab and GitHub releases at startup (and from Help › Check for updates). Installed Windows copies download the new installer and update in place, restarting afterwards; macOS and Android open the download. A release can be skipped so the startup check stops offering it; automatic checks and pre-releases can be turned on or off in Options.
- **Git sign-in dialog**: when a push, pull, fetch or clone over HTTPS has no login or the login is rejected, the app asks for a username and password or access token and retries. Sign-ins can be remembered (secure storage), apply to a whole server or just one repository, and a token can also become the server's API token. Saved sign-ins can be forgotten in Options.

### 🐞 Bug fixes
- The window no longer opens taller than the screen on scaled displays.
- macOS build: fixed the packaging script under macOS's bash 3.2, and unsigned builds no longer look for a signing certificate.

## 0.1.0
### ✨ Features and improvements
- First release: a GitHub Desktop-style client for GitLab and GitHub. Changes and History tabs, line-level commits, branch management, merge/pull request creation, CI status, cloning, and a repositories folder that picks up existing checkouts.
- Recognises whether each repository is hosted on GitLab or GitHub (asking the server when needed) and shows that host's wording and links.
