# tktco.UdonSharpLinter

## 0.5.0

### Minor Changes

- [#23](https://github.com/tktcorporation/vrchat-udonsharp-linter/pull/23) [`253ef8f`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/253ef8fef7977a9d411883aa37ffcf1cd5f3d0bd) Thanks [@tktcorporation](https://github.com/tktcorporation)! - Add new lint checks inspired by agent-skills-vrc-udon

  New checks added:

  - UDON032: Generic collection types (`List<T>`, `Dictionary<K,V>`, `HashSet<T>`, `Queue<T>`, `Stack<T>`) are not supported in UdonSharp
  - UDON033: LINQ (`using System.Linq;`) is not supported in UdonSharp
  - UDON034: Lambda expressions, `delegate` declarations, and C# `event` fields are not supported in UdonSharp
  - UDON035: Coroutines (`yield return` / `StartCoroutine()`) are not supported in UdonSharp
  - UDON036: `UnityEvent.AddListener()` cannot reliably register UdonSharp methods at runtime
  - UDON037: `GetComponent<UdonBehaviour>()` is not supported; use `(UdonBehaviour)GetComponent(typeof(UdonBehaviour))` instead
  - UDON038: `[UdonSynced]` fields conflict with `[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]`
  - UDON039: Manual sync mode with `[UdonSynced]` fields requires an explicit `RequestSerialization()` call
  - UDON040: Warns when a behaviour has an excessive number of `[UdonSynced]` fields (network bandwidth budget)
  - UDON041: Warns when `int[]`/`float[]` fields are synced, suggesting `byte[]`/`short[]` instead

  Also added a new "Networking and Synchronization" section to the README, and unit tests covering all new checks.

### Patch Changes

- [#35](https://github.com/tktcorporation/vrchat-udonsharp-linter/pull/35) [`8f21526`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/8f215261aaf504434878cca0c568064cc137d62c) Thanks [@tktcorporation](https://github.com/tktcorporation)! - Fix attribute matching to also resolve alias-qualified names (e.g. `[global::NetworkCallable]`) (#29, thanks @dchaudhari7177!)

  The exact-name attribute match introduced for #27 extracted an attribute's
  simple name by splitting `a.Name.ToString()` on `.`, which missed
  alias-qualified attribute names (`global::X`) since they contain no `.` to
  split on — such attributes were silently not recognized. `IsAttributeNameMatch`
  now resolves the simple name from the attribute's syntax node directly
  (`QualifiedNameSyntax`/`AliasQualifiedNameSyntax`/`SimpleNameSyntax`), fixing
  this case. Also adds the first test coverage for `[NetworkCallable]` attribute
  matching (decoy, exact, explicit `Attribute` suffix, namespace-qualified, and
  alias-qualified), which had none previously despite being called out in #27.

- [#34](https://github.com/tktcorporation/vrchat-udonsharp-linter/pull/34) [`cc9738d`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/cc9738dbfa14132832f03fac2da6bc1f5f5e125e) Thanks [@tktcorporation](https://github.com/tktcorporation)! - Add automated test coverage for cross-file semantic checks (#31)

  `CheckCrossFileFieldAccess`, `CheckCrossFileMethodInvocation`,
  `CheckUdonBehaviourSerializableClassUsage`, `CheckStaticMethodFieldAccess`,
  `BuildCallGraph`, and `BuildTypeReferenceGraph` previously had zero
  automated tests, since they require multiple `SyntaxTree`s in one
  `Compilation` to exercise. Added a new `Program.AnalyzeCodeMultiFile(params
(string path, string source)[] files)` test helper that mirrors the
  multi-file pipeline in `Main()`, plus a `CrossFileSemanticCheckTests` suite
  covering these checks (including negative cases for same-file access and
  unreachable static methods).

- [#34](https://github.com/tktcorporation/vrchat-udonsharp-linter/pull/34) [`09b81c8`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/09b81c8ea814aab0d6295fd1d4de7aedef540569) Thanks [@tktcorporation](https://github.com/tktcorporation)! - Fix HasAttribute/GetUdonBehaviourSyncMode using substring matching (#27)

  `HasAttribute()` and `GetUdonBehaviourSyncMode()` matched attribute usages
  via `.Contains(...)`, so a custom attribute whose name merely _contained_
  a checked-for name as a substring (e.g. `[UdonSyncedMetadata]` vs.
  `[UdonSynced]`, or a decoy `[FooUdonBehaviourSyncModeBar]`) could be
  mistaken for the real attribute, causing UDON038-041/the sync-mode checks
  to misfire. Both helpers now compare against the attribute's simple name
  exactly (accounting for the optional `Attribute` suffix).

- [#34](https://github.com/tktcorporation/vrchat-udonsharp-linter/pull/34) [`d36e5b2`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/d36e5b2ce3b1e6a734f5995fc8827b45328e5c9b) Thanks [@tktcorporation](https://github.com/tktcorporation)! - Fix semantic analysis missing Unity/VRC type info in real projects (#28)

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

- [#34](https://github.com/tktcorporation/vrchat-udonsharp-linter/pull/34) [`c7f976f`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/c7f976f94760c8504dedae9d7cc87a6713f5f2bc) Thanks [@tktcorporation](https://github.com/tktcorporation)! - Verify and lock in that globbing Library/ScriptAssemblies is safe with stale project DLLs

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

## 0.4.0

### Minor Changes

- [#17](https://github.com/tktcorporation/vrchat-udonsharp-linter/pull/17) [`e92d528`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/e92d528423b61c7153e745d56356c1c87756a174) Thanks [@tktcorporation](https://github.com/tktcorporation)! - Add detection for static field access on user-defined types (UDON031)

  UdonSharp does not support static fields on user-defined types. This change adds detection for:

  1. Static field access from UdonSharpBehaviour to user-defined types
  2. Static field definitions in utility classes that are referenced from UdonSharp code

  The following are still allowed:

  - `const` fields (compile-time constants)
  - `static readonly` fields
  - Unity/VRC/System built-in type static fields

  This fixes cases where utility classes (not inheriting UdonSharpBehaviour) define static fields and are referenced from UdonSharp code, which causes UdonSharp compilation errors.

### Patch Changes

- [#20](https://github.com/tktcorporation/vrchat-udonsharp-linter/pull/20) [`7843422`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/7843422e4e768ee6174b8e11804d18749d174972) Thanks [@tktcorporation](https://github.com/tktcorporation)! - Improve README: move installation instructions to the top, use table format for error code listing, add badges and Japanese descriptions

## 0.3.1

### Patch Changes

- [#18](https://github.com/tktcorporation/vrchat-udonsharp-linter/pull/18) [`ce955fd`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/ce955fd3de4dab75a76251e4c2f47ff5c71cebce) Thanks [@tktcorporation](https://github.com/tktcorporation)! - Fix false positives for Editor scripts on Unix/Linux systems

  - Add support for Unix-style forward slash path separators (`/`) in addition to Windows-style backslashes (`\`)
  - This fixes the issue where Editor scripts were incorrectly analyzed on non-Windows platforms
  - Also applies the fix to Temp, Library, obj, bin, TestScripts, Tests, and Test directory exclusions

## 0.3.0

### Minor Changes

- [#15](https://github.com/tktcorporation/vrchat-udonsharp-linter/pull/15) [`94179c3`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/94179c3d26cf49965b700c2862d4e6d55883efa4) Thanks [@tktcorporation](https://github.com/tktcorporation)! - Remove deprecated lint rules for features now supported in UdonSharp 1.0+

  Based on the official UdonSharp documentation review:

  **Removed Checks:**

  - UDON015 (Properties): User-defined properties are now fully supported in UdonSharp 1.0+
  - UDON028 (Null Coalescing Operators): The `??` and `??=` operators are now supported

  **Unchanged Checks:**
  All other checks remain valid as these features are still unsupported:

  - Try/catch (UDON001), throw (UDON002), local functions (UDON003)
  - Constructors (UDON005), generics (UDON006, UDON018)
  - Object/collection initializers (UDON007, UDON008)
  - Multidimensional arrays (UDON009), static fields (UDON011)
  - Nested types (UDON012), method overloads (UDON016)
  - Interfaces (UDON017), null conditional operator `?.` (UDON027)
  - Async/await (UDON029), goto (UDON030)

  References:

  - https://udonsharp.docs.vrchat.com/
  - https://udonsharp.docs.vrchat.com/news/release-1.0.0b3/

## 0.2.1

### Patch Changes

- [#13](https://github.com/tktcorporation/vrchat-udonsharp-linter/pull/13) [`9124c1b`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/9124c1b65921a169eb9dd295d86d64c2c161f4d6) Thanks [@tktcorporation](https://github.com/tktcorporation)! - Unify package name to tktco.UdonSharpLinter and improve READMEGenerator

  - Fix package name consistency across package.json, CHANGELOG.md, and changeset files
  - READMEGenerator now extracts error codes directly from method bodies using Roslyn
  - Automatically follows method calls to find error codes in child methods
  - Errors if Check methods use uncategorized error codes

## 0.2.0

### Minor Changes

- [#11](https://github.com/tktcorporation/vrchat-udonsharp-linter/pull/11) [`edd5afd`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/edd5afda1bb9665a01572041bcfe19248f171c04) Thanks [@tktcorporation](https://github.com/tktcorporation)! - Add new lint checks inspired by udon-analyzer

  New checks added:

  - UDON026: SendCustomEvent method name validation - Detects typos and missing methods in SendCustomEvent, SendCustomEventDelayedSeconds, SendCustomEventDelayedFrames, and SendCustomNetworkEvent calls
  - UDON027: Null conditional operator (?.) detection - Detects usage of unsupported ?. operator
  - UDON028: Null coalescing operator (??, ??=) detection - Detects usage of unsupported ?? and ??= operators
  - UDON029: Async/await detection - Detects usage of unsupported async methods and await expressions
  - UDON030: Goto/label statement detection - Detects usage of unsupported goto and labeled statements

  Also added:

  - Test project with unit tests for new checks
  - CI workflow for running tests on PRs

### Patch Changes

- [#9](https://github.com/tktcorporation/vrchat-udonsharp-linter/pull/9) [`127d7e2`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/127d7e21319109d531f560cff6de89acc72a05c7) Thanks [@tktcorporation](https://github.com/tktcorporation)! - Simplify README for end users and use global tool command in examples

## 0.1.3

### Patch Changes

- [#6](https://github.com/tktcorporation/vrchat-udonsharp-linter/pull/6) [`31ad523`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/31ad5234142201496daa7280d2c6e83a1feefc51) Thanks [@tktcorporation](https://github.com/tktcorporation)! - Add CI check to validate README is up-to-date with auto-generated content

- [#8](https://github.com/tktcorporation/vrchat-udonsharp-linter/pull/8) [`0a3ef3c`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/0a3ef3c225f39b71b735b74ce3000da09cc13801) Thanks [@tktcorporation](https://github.com/tktcorporation)! - Fix incorrect paths and command names in README documentation

## 0.1.2

### Patch Changes

- [`c3dfc54`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/c3dfc5466881a1d9fedaf3d8bf1ce33f5845c07e) Thanks [@tktcorporation](https://github.com/tktcorporation)! - リリースノート抽出処理の修正

  - インライン Node.js コードを外部スクリプト（scripts/extract-release-notes.js）に移動
  - 正規表現のエスケープ問題を解決し、CHANGELOG.md からの抽出が正しく動作するように修正
  - README から旧パッケージ（VRChat.UdonSharp.Linter）に関する注記を削除

## 0.1.1

### Patch Changes

- [`0ce0fc6`](https://github.com/tktcorporation/vrchat-udonsharp-linter/commit/0ce0fc63bb790a1e24209259a3fd515bee00b642) Thanks [@tktcorporation](https://github.com/tktcorporation)! - リリースワークフローの改善

  - GitHub Release の description に CHANGELOG.md の内容を使用するように変更
  - @changesets/changelog-github を導入し、PR リンク・コミットリンク・貢献者表記付きのリッチなリリースノートを生成
  - タグ存在チェックをリモート対応に修正（`git rev-parse` → `git ls-remote`）
  - GitHub Release が既に存在する場合はスキップするように改善

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
