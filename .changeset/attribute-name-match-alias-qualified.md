---
"tktco.UdonSharpLinter": patch
---

Fix attribute matching to also resolve alias-qualified names (e.g. `[global::NetworkCallable]`) (#29, thanks @dchaudhari7177!)

The exact-name attribute match introduced for #27 extracted an attribute's
simple name by splitting `a.Name.ToString()` on `.`, which missed
alias-qualified attribute names (`global::X`) since they contain no `.` to
split on — such attributes were silently not recognized. `IsAttributeNameMatch`
now resolves the simple name from the attribute's syntax node directly
(`QualifiedNameSyntax`/`AliasQualifiedNameSyntax`/`SimpleNameSyntax`), fixing
this case. Also adds the first test coverage for `[NetworkCallable]` attribute
matching (decoy, exact, explicit `Attribute` suffix, namespace-qualified, and
alias-qualified), which had none previously despite being called out in #27.
