---
"tktco.UdonSharpLinter": minor
---

Add detection for static field access on user-defined types (UDON031)

UdonSharp does not support static fields on user-defined types. This change adds detection for:

1. Static field access from UdonSharpBehaviour to user-defined types
2. Static field definitions in utility classes that are referenced from UdonSharp code

The following are still allowed:
- `const` fields (compile-time constants)
- `static readonly` fields
- Unity/VRC/System built-in type static fields

This fixes cases where utility classes (not inheriting UdonSharpBehaviour) define static fields and are referenced from UdonSharp code, which causes UdonSharp compilation errors.
