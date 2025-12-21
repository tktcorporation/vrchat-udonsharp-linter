# vrchat-udonsharp-linter

## 1.1.2

### Patch Changes

- 9380fc5: Move release logic to workflow YAML for better maintainability

  - Remove separate release.js script
  - Implement tag creation and GitHub Release directly in workflow
  - Simplify package.json by removing release script
  - All release logic now contained in release.yml

## 1.1.1

### Patch Changes

- 19c3a32: Simplify release workflow by using changeset tag

  - Replace custom create-github-release.js script with built-in changeset tag command
  - Let changesets action handle GitHub Release creation automatically
  - Reduce maintenance burden and align with standard changesets workflow pattern

## 1.1.0

### Minor Changes

- d51ab91: Add automated release workflow with changesets and OIDC authentication

  - Implement changesets for automated version management
  - Add OIDC authentication for secure NuGet publishing
  - Add automated GitHub Release creation
  - Add changeset validation for pull requests
