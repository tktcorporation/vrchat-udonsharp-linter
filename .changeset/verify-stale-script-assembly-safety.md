---
"tktco.UdonSharpLinter": patch
---

Verify and lock in that globbing Library/ScriptAssemblies is safe with stale project DLLs

`Library/ScriptAssemblies` contains not just the Unity/VRC SDK but also the
project's own previously-compiled script assemblies (e.g.
`Assembly-CSharp.dll`), so after #28's `*.dll` glob, a metadata reference
can contain a type with the exact same name as one of the `.cs` files also
being parsed as source. Verified via a regression test that this doesn't
corrupt symbol resolution: the C# compiler prefers the source-declared type
over the metadata one for a same-named type (CS0436), so
`CheckCrossFileFieldAccess` and friends keep resolving to the correct
source symbol. `CreateCompilation` now accepts an optional
`scriptAssembliesDirOverride` (and `AnalyzeCodeMultiFile` a matching
overload) so this scenario is testable without mutating the process's
current directory.
