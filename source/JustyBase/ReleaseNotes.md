# JustyBase 1.0.0-rc.8

- VS Code-style interaction between SQL autocomplete and inline ghost text.
- The selected completion item now drives the FIM prompt and updates with arrow-key navigation.
- First `Tab` accepts autocomplete; second `Tab` accepts the remaining AI continuation.
- Release packaging disables the shared compiler (`UseSharedCompilation=false`) to avoid cross-platform artifact file locks during publish.
