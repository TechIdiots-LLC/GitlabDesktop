# Syncing

The third toolbar button sends and receives changes. Its label shows what it will do next:

| Button | When | What it does |
| --- | --- | --- |
| **Publish branch** | The branch isn't on the server yet | Pushes it and sets it to track the server's copy. |
| **Pull origin** ↓2 | The server has commits you don't (↓ how many) | Pulls them in. If you also have unpushed commits (↑), they're merged. |
| **Push origin** ↑1 | You have commits the server doesn't (↑ how many) | Pushes them. Tags on those commits go too. |
| **Fetch origin** | You're up to date | Checks the server for new commits. The subtitle says when it last checked. |

The same actions are in the **Repository** menu: **Push** (Ctrl+P), **Pull** (Ctrl+Shift+P) and **Fetch**
(Ctrl+Shift+T).

## Newer commits on the server

If someone else pushed to the branch since you last fetched, the server refuses your push. The app says so and offers
**Fetch**. After fetching, the toolbar shows **Pull origin** with both counts (for example ↓2 ↑1): pull to combine
their commits with yours, then push.

## Diverged branches

If both you and the server have new commits and you push, you're asked to choose:

- **Pull (merge remote changes)**: the safe choice. Pull, then push again.
- **Force push (overwrite remote)**: replaces the server's branch with yours. Use this only after rewriting your own
  history, for example after amending a pushed commit or rebasing. It's done with `--force-with-lease`, which refuses
  if the server has commits you haven't fetched.

## Pull behaviour

Pulling uses your git `pull.rebase` setting if you have one. Without it, the app merges, because newer versions of git
otherwise refuse to pull a branch that has diverged. If the merge has conflicts, see
[Conflicts](branches-and-requests.md#conflicts).

## Uncommitted changes in the way

If you have uncommitted changes to files the pull would change, git won't pull. The app offers **Stash and pull**: it
sets your changes aside, pulls, and puts them back on top. If some of them clash with the pulled commits, those files
are marked as [conflicted](branches-and-requests.md#conflicts) for you to resolve, and a copy of your changes is kept
under **Branch › Restore stashed changes…** so nothing is lost. If the pull itself fails, your changes stay in that
stash.

## Signing in

If the server needs a sign-in the app doesn't have, or rejects the saved one, you're
[asked to sign in](accounts.md#when-git-asks-you-to-sign-in) and the action is retried.
