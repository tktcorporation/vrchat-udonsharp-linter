# Contributing to VRChat UdonSharp Linter

## Release Process with Changesets

This project uses [Changesets](https://github.com/changesets/changesets) to manage versions and releases.

### For Contributors

When you make changes that should trigger a new release, you need to create a changeset:

1. **Make your changes** to the codebase

2. **Create a changeset**:
   ```bash
   npm run changeset
   ```

3. **Answer the prompts**:
   - Select the type of change (patch/minor/major)
   - Describe your changes (this will go into the CHANGELOG)

4. **Commit the changeset file** along with your changes:
   ```bash
   git add .changeset/*.md
   git commit -m "feat: your feature description"
   git push
   ```

### Types of Changes

- **Patch** (0.0.X): Bug fixes, small improvements
- **Minor** (0.X.0): New features, non-breaking changes
- **Major** (X.0.0): Breaking changes

### Skipping Changesets

If your PR doesn't need a release (e.g., documentation updates, CI changes), add the `skip-changeset` label to your PR.

### How Releases Work

1. **Changesets Merged**: When PRs with changesets are merged to `master`, a "Version Packages" PR is automatically created by the release workflow
2. **Version PR Updates**:
   - `package.json` version
   - `.csproj` version (via `scripts/update-csproj-version.js`)
   - `CHANGELOG.md`
3. **Release Creation**: When the "Version Packages" PR is merged:
   - A git tag is created (e.g., `v1.0.0`)
   - A GitHub Release is created automatically with changelog notes
4. **NuGet Publishing**: The GitHub Release triggers the `publish.yml` workflow:
   - Builds the .NET project
   - Authenticates to NuGet via OIDC
   - Publishes the package to NuGet

### For Maintainers

To release a new version:

1. Review and merge the "Version Packages" PR created by the Changesets bot
2. The release and NuGet publish will happen automatically
3. No manual tagging or version bumping needed!

## Development Setup

```bash
# Install dependencies
npm install

# Create a changeset
npm run changeset

# Version packages (usually done by CI)
npm run version
```
