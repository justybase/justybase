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

## Dependency and supply-chain checks

CI restores the full solution and fails when `dotnet list package --vulnerable --include-transitive`
reports any known vulnerable NuGet package (any severity). Security-relevant pins, such as
`System.Security.Cryptography.Pkcs`, are kept in the project files with a comment that names the
advisory they address.

The bundled third-party package inventory is generated from the production dependency graphs:

```powershell
pwsh ./scripts/generate-third-party-notices.ps1
```

`THIRD_PARTY_NOTICES.md` must be regenerated after dependency changes; CI fails when it is stale.

## Release integrity

Release artifacts are published as GitHub Release assets together with a `SHA256SUMS` manifest
generated from the exact files that are uploaded. Download the manifest, run `sha256sum -c
SHA256SUMS`, and compare the result before running an installer.

Windows Authenticode signing and macOS notarization are **not configured yet**. Treat unsigned
artifacts accordingly and prefer a verified hash over the file name.

## Secrets and feeds

Do not commit update-feed pre-auth URLs, database credentials, or API keys. The update-feed path has been removed from the app.

### Rotating a leaked Object Storage / Velopack feed URL

If a pre-authenticated Oracle Object Storage (or similar) URL was ever committed:

1. Invalidate / rotate the pre-auth token in the cloud console immediately.
2. No update-feed URL should be introduced in source or local configuration.
3. Treat any historical URL in git history as compromised even after removal from `HEAD`.
