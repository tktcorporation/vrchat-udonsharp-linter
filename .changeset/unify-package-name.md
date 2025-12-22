---
"tktco.UdonSharpLinter": patch
---

Unify package name to tktco.UdonSharpLinter and improve READMEGenerator

- Fix package name consistency across package.json, CHANGELOG.md, and changeset files
- READMEGenerator now extracts error codes directly from method bodies using Roslyn
- Automatically follows method calls to find error codes in child methods
- Errors if Check methods use uncategorized error codes
