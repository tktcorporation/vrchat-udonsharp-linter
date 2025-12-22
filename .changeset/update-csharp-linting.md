---
"tktco.UdonSharpLinter": minor
---

Remove deprecated lint rules for features now supported in UdonSharp 1.0+

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
