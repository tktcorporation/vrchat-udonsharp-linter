---
"vrchat-udonsharp-linter": patch
---

Simplify release workflow by using changeset tag

- Replace custom create-github-release.js script with built-in changeset tag command
- Let changesets action handle GitHub Release creation automatically
- Reduce maintenance burden and align with standard changesets workflow pattern
