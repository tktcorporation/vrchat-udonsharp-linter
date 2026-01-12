---
"tktco.UdonSharpLinter": patch
---

Fix false positives for Editor scripts on Unix/Linux systems

- Add support for Unix-style forward slash path separators (`/`) in addition to Windows-style backslashes (`\`)
- This fixes the issue where Editor scripts were incorrectly analyzed on non-Windows platforms
- Also applies the fix to Temp, Library, obj, bin, TestScripts, Tests, and Test directory exclusions
