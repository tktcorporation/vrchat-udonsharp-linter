---
"tktco.UdonSharpLinter": patch
---

Fix semantic analysis missing Unity/VRC type info in real projects (#28)

`CreateCompilation` used to only look for three hardcoded DLL names
(`UnityEngine.dll`, `VRC.SDKBase.dll`, `UdonSharp.Runtime.dll`) under
`Library/ScriptAssemblies`. Modern Unity (2018.1+) splits engine and SDK
types across many per-module assemblies (e.g. `UnityEngine.CoreModule.dll`,
`VRC.Udon.dll`), so a monolithic `UnityEngine.dll` typically doesn't exist
in a real, already-opened VRChat project — the old code silently loaded
none of these references in the common case.

`CreateCompilation` now globs every `*.dll` under `Library/ScriptAssemblies`
instead, so semantic checks that depend on resolving Unity/VRC types (e.g.
`UnityEvent`, `UdonBehaviour`) actually get that type information when run
inside a real project directory.
