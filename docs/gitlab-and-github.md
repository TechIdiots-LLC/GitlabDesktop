# GitLab, GitHub and other servers

The app works with repositories on any git server. What it can do beyond git depends on the kind of server a
repository's remote is on:

| | GitLab | GitHub | Other servers |
| --- | --- | --- | --- |
| Commit, branch, push, pull | ✓ | ✓ | ✓ |
| View on server, compare, view branch | ✓ | ✓ | View on server only |
| Requests | Merge requests (`!12`) | Pull requests (`#12`) | – |
| CI | Pipelines | Actions / checks | – |
| Issues | ✓ | ✓ | – |
| Create requests in the app, CI status, project list when cloning | With a GitLab [account](accounts.md) | With a GitHub [account](accounts.md) | – |

## How the server is recognised

No server names are built in except `github.com`, so self-hosted GitLab servers and GitHub Enterprise work under any
name. For each repository's remote, the app decides in this order:

1. **An account in Options** for that server says what it is.
2. **Asking the server**, once per server, with the answer remembered. GitLab answers its API with an `X-Gitlab-Meta`
   header even without signing in, and GitHub Enterprise answers `/api/v3/meta`.
3. **The server's name**, if it can't be reached: `github.com`, or a name containing `gitlab` or `github`.

If none of these settle it, the server is treated as a plain git server: menus use its name (**View on
git.example.com**), and the app suggests adding an account if you try something that needs one. The next time git asks
you to sign in to that server, the prompt also asks for its **Server type**. You can set or correct the type of any
account in **Options › Accounts**, and an account's type always takes priority.

Remotes over SSH (`git@gitlab.example.com:group/project.git`) are recognised the same way, using the server's name.
