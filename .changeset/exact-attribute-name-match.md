---
"tktco.UdonSharpLinter": patch
---

Fix HasAttribute/GetUdonBehaviourSyncMode using substring matching (#27)

`HasAttribute()` and `GetUdonBehaviourSyncMode()` matched attribute usages
via `.Contains(...)`, so a custom attribute whose name merely *contained*
a checked-for name as a substring (e.g. `[UdonSyncedMetadata]` vs.
`[UdonSynced]`, or a decoy `[FooUdonBehaviourSyncModeBar]`) could be
mistaken for the real attribute, causing UDON038-041/the sync-mode checks
to misfire. Both helpers now compare against the attribute's simple name
exactly (accounting for the optional `Attribute` suffix).
