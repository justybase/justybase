# Security Policy

## Supported versions

Security fixes are applied on the latest published release and the `main`/`master` branch.

## Reporting a vulnerability

Please **do not** open a public issue for sensitive reports.

Email the maintainer via the contact on [GitHub profile](https://github.com/justybase), or open a private security advisory on the repository if enabled.

Include:

- Affected version / commit
- Impact and reproduction steps
- Whether a fix is already known

## Secrets and feeds

Do not commit update-feed pre-auth URLs, database credentials, or API keys. The update-feed path has been removed from the app.

### Rotating a leaked Object Storage / Velopack feed URL

If a pre-authenticated Oracle Object Storage (or similar) URL was ever committed:

1. Invalidate / rotate the pre-auth token in the cloud console immediately.
2. No update-feed URL should be introduced in source or local configuration.
3. Treat any historical URL in git history as compromised even after removal from `HEAD`.
