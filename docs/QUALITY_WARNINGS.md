# Build warnings policy

The repository enables the complete .NET analyzer set. The warning audit is run
with:

```powershell
./scripts/audit-local-warnings.ps1 -Configuration Debug -NoIncremental
```

The report excludes warnings emitted by the sibling `ProDataGrid` solution. It
does not change that project and it does not change GitHub Actions.

## Warnings treated as defects

These categories remain active and should be fixed in code whenever possible:

- compiler nullability warnings (`CS8600`, `CS8602`, `CS8603`, `CS8625`);
- resource ownership and disposal (`CA2000`, `CA2213`);
- SQL command construction (`CA2100`);
- native library loading (`CA5392`);
- cryptography and randomness (`CA5394`, `CA5401`);
- cancellation propagation and asynchronous resource handling.

Recent fixes explicitly close or transfer ownership for HTTP requests,
processes, clipboard data, database readers, spill stores and Avalonia drag
data. P/Invoke declarations use `DefaultDllImportSearchPaths`.

## Documented exceptions

- `CA1515` is silent for the desktop application because public types are
  consumed by Avalonia XAML, dependency injection and plugin composition.
- `CA1707` is silent in test projects because underscore-separated test names
  describe scenarios and are intentionally readable in test output.
- `CA2007` is silent in the Avalonia application because continuations are
  intentionally resumed on the UI synchronization context.
- `AVLN3001` is suppressed only for `JustyBase`: its views are created through
  dependency injection and therefore intentionally have parameterized
  constructors instead of public parameterless constructors.

These are scoped suppressions with comments. Security, nullability and
ownership warnings are not globally suppressed.
