---
"tktco.UdonSharpLinter": patch
---

Add automated test coverage for cross-file semantic checks (#31)

`CheckCrossFileFieldAccess`, `CheckCrossFileMethodInvocation`,
`CheckUdonBehaviourSerializableClassUsage`, `CheckStaticMethodFieldAccess`,
`BuildCallGraph`, and `BuildTypeReferenceGraph` previously had zero
automated tests, since they require multiple `SyntaxTree`s in one
`Compilation` to exercise. Added a new `Program.AnalyzeCodeMultiFile(params
(string path, string source)[] files)` test helper that mirrors the
multi-file pipeline in `Main()`, plus a `CrossFileSemanticCheckTests` suite
covering these checks (including negative cases for same-file access and
unreachable static methods).
