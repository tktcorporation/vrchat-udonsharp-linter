# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

UdonSharpLinterCLI is a static code analyzer for UdonSharp scripts in VRChat projects. It detects language features and patterns not supported by UdonSharp at compile time. The tool is published as a .NET global tool on NuGet.

## Common Commands

```bash
# Build the project
dotnet build src/tktco.UdonSharpLinter/tktco.UdonSharpLinter.csproj

# Run the linter on a directory
dotnet run --project src/tktco.UdonSharpLinter/tktco.UdonSharpLinter.csproj -- <directory_path>

# Run with test scripts excluded
dotnet run --project src/tktco.UdonSharpLinter/tktco.UdonSharpLinter.csproj -- <directory_path> --exclude-test-scripts

# Regenerate README.md from source code documentation
dotnet run --project src/tktco.UdonSharpLinter/tktco.UdonSharpLinter.csproj -- --generate-readme

# Create a changeset for versioning (required for PRs)
npm run changeset

# Apply version changes from changesets
npm run version
```

## Architecture

### Core Components

- **Program.cs** (`src/tktco.UdonSharpLinter/Program.cs`): Main entry point containing all lint checks. Uses Roslyn (Microsoft.CodeAnalysis.CSharp) for:
  - Syntax tree analysis for language feature restrictions
  - Semantic model analysis for cross-file type checking
  - Compilation-wide call graph analysis for static method validation

- **READMEGenerator.cs** (`src/tktco.UdonSharpLinter/READMEGenerator.cs`): Auto-generates README.md from XML documentation comments in Program.cs. The README is derived from `<summary>` tags on check methods and the `LintErrorCodes` class.

### Error Code System

Error codes are defined in `LintErrorCodes` class (Program.cs:309-337) and follow the pattern `UDON###`:
- 1-18: Basic language feature restrictions
- 13-19: API and attribute restrictions
- 20-25: Cross-file and semantic analysis

### Adding New Lint Rules

1. Add a new error code constant to `LintErrorCodes` class
2. Create a `Check*` method with XML documentation (`<summary>` tag)
3. Call the check method from `LintFile()` or appropriate location
4. Regenerate README.md with `--generate-readme`

### File Filtering

The linter automatically excludes:
- `Temp`, `Library`, `obj`, `bin` directories
- Editor scripts (`\Editor\` paths)
- Test scripts when `--exclude-test-scripts` is used (`\TestScripts\`, `\Tests\`, `\Test\`)

### UdonSharp Detection

Only files containing both are analyzed:
1. `using UdonSharp;` directive
2. Class inheriting from `UdonSharpBehaviour`

## CI/CD

- **readme-check.yml**: Validates README.md is up-to-date with source code on PRs
- **changeset-check.yml**: Requires a changeset file for PRs (skip with `skip-changeset` label)
- **release.yml**: On master merge, creates release PRs via changesets, then publishes to NuGet

## Versioning

Uses [Changesets](https://github.com/changesets/changesets) for version management. When versioning:
- `npm run version` updates both package.json and tktco.UdonSharpLinter.csproj versions via `scripts/update-csproj-version.js`
