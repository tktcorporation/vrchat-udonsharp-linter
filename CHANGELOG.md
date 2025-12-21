# tktco-udonsharp-linter

## 0.1.0

### Breaking Changes

- Package renamed from `VRChat.UdonSharp.Linter` to `tktco.UdonSharpLinter`
- Namespace changed from `UdonSharpLinterCLI` to `tktco.UdonSharpLinter`
- Version reset to 0.1.0 to indicate pre-release status

### Migration Guide

If you were using the previous package `VRChat.UdonSharp.Linter`, please uninstall it and install the new package:

```bash
dotnet tool uninstall VRChat.UdonSharp.Linter
dotnet tool install tktco.UdonSharpLinter
```

The command name remains the same: `udonsharp-lint`

### Features

- Static analysis for VRChat UdonSharp scripts
- Detection of common UdonSharp restrictions and unsupported features
- CLI tool for linting UdonSharp code
- All features from VRChat.UdonSharp.Linter v1.1.2
