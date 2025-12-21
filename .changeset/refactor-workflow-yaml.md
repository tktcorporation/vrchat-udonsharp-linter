---
"vrchat-udonsharp-linter": patch
---

Move release logic to workflow YAML for better maintainability

- Remove separate release.js script
- Implement tag creation and GitHub Release directly in workflow
- Simplify package.json by removing release script
- All release logic now contained in release.yml
