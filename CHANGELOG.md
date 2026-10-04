# Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._

### 🐞 Bug fixes
- _...Add new stuff here..._
- Dragging the dividers between the lists and the diff now resizes them on Windows (the cursor changed, but the layout didn't update), and a divider's width is only saved after an actual drag.

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
