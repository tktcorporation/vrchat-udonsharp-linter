---
"tktco.UdonSharpLinter": patch
---

Convert UDON036/037/038 to hybrid syntax+semantic checks (#25)

Now that #28 makes Unity/VRC assemblies resolvable via `Library/ScriptAssemblies`, converted the remaining syntax-only heuristics flagged in #25 to also resolve via the semantic model, each as an additive fallback alongside the existing text-based check (so behavior is unchanged when the real SDK types aren't resolvable, e.g. outside a real Unity project):

- **UDON037** (`GetComponent<UdonBehaviour>()`): now also resolves the type argument's symbol, catching type aliases like `using UB = VRC.Udon.UdonBehaviour;`.
- **UDON036** (`UnityEvent.AddListener()`): now also resolves the receiver's type and walks its base types for `UnityEngine.Events.UnityEventBase`, catching UnityEvent-typed fields that don't happen to start with `"on"` (e.g. `readyEvent.AddListener(...)`). Also fixed a separate gap where `button.onClick?.AddListener(...)` (a null-conditional call, represented via `MemberBindingExpressionSyntax`) wasn't detected at all.
- **UDON038** (sync mode conflict / manual sync warning): now also compares the sync-mode argument's constant value against the real `BehaviourSyncMode` enum's `None`/`NoVariableSync`/`Manual` members (when resolvable), catching indirection through aliases or constants that the textual last-segment check can't. The enum's actual integer values are read dynamically rather than hardcoded, since they can differ across SDK versions.
