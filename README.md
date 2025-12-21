# UdonSharpLinterCLI

A static code analyzer for UdonSharp scripts in VRChat projects. This tool detects language features and patterns that are not supported by UdonSharp at compile time.

## Features

UdonSharpLinterCLI performs comprehensive checks for UdonSharp restrictions, including:

### Basic Language Features
- Collection initializers (UDON008)
- Constructors (UDON005)
- Generic classes (UDON006, UDON018)
- Generic methods (UDON006, UDON018)
- Local functions (UDON003)
- Multidimensional arrays (UDON009)
- Nested types (UDON012)
- Object initializers (UDON007)
- Static fields (UDON011, UDON021)
- Throw statements (UDON002)
- Try Catch statements (UDON001)

### API and Attribute Restrictions
- Interfaces (UDON017)
- Method Overloads (UDON016)
- Network Callable methods (UDON013)
- Text Mesh Pro APIs (UDON014)

### Cross-file and Semantic Analysis
- Cross File Field Access (UDON020)
- Cross File Method Invocation (UDON022)
- Static Method Field Access (UDON021)
- Udon Behaviour Serializable Class Usage (UDON025)

## Usage

```bash
UdonSharpLinterCLI <directory_path> [--exclude-test-scripts]
```

### Arguments

- `<directory_path>`: Path to the directory containing UdonSharp scripts to analyze
- `--exclude-test-scripts`: (Optional) Exclude scripts in TestScripts, Tests, or Test directories

### Examples

```bash
# Analyze all UdonSharp scripts in Assets
UdonSharpLinterCLI Assets

# Analyze excluding test scripts
UdonSharpLinterCLI Assets --exclude-test-scripts
```

## Output Format

The tool outputs errors and warnings in a standard compiler format:

```
path/to/file.cs(line,column): error UDON###: Error message
path/to/file.cs(line,column): warning UDON###: Warning message
```

This format is compatible with most IDEs and CI/CD tools.

## Exit Codes

- `0`: No errors found (warnings may be present)
- `1`: Errors found or execution failed

## Requirements

- .NET 6.0 or later
- Unity project with UdonSharp

## Build

```bash
dotnet build
```

## Integration Examples

### Visual Studio Code (tasks.json)

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "UdonSharp Lint",
      "type": "shell",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "Tools/UdonSharpLinterCLI/UdonSharpLinterCLI.csproj",
        "--",
        "${workspaceFolder}/Assets"
      ],
      "problemMatcher": {
        "owner": "udonsharp",
        "fileLocation": ["relative", "${workspaceFolder}"],
        "pattern": {
          "regexp": "^(.+)\\((\\d+),(\\d+)\\):\\s+(error|warning)\\s+UDON(\\d+):\\s+(.+)$",
          "file": 1,
          "line": 2,
          "column": 3,
          "severity": 4,
          "code": 5,
          "message": 6
        }
      }
    }
  ]
}
```

### GitHub Actions

```yaml
- name: Run UdonSharp Linter
  run: dotnet run --project Tools/UdonSharpLinterCLI/UdonSharpLinterCLI.csproj -- Assets --exclude-test-scripts
```

### mise (mise.toml)

```toml
[tasks."lint-udon"]
run = "dotnet run --project Tools/UdonSharpLinterCLI/UdonSharpLinterCLI.csproj -- Assets"
```

## Implementation Details

The linter uses Roslyn (Microsoft.CodeAnalysis.CSharp) for:
- Syntax tree analysis for language feature restrictions
- Semantic model analysis for cross-file type checking
- Compilation-wide call graph analysis for static method validation

### Excluded Files

The linter automatically excludes:
- Temp, Library, obj, bin directories
- Editor scripts
- Test scripts (when `--exclude-test-scripts` is used)

### UdonSharp Detection

Only files that contain both:
1. `using UdonSharp;` directive
2. `UdonSharpBehaviour` class inheritance

are analyzed as UdonSharp scripts.

## Generating This README

This README is auto-generated from source code documentation. To regenerate:

```bash
dotnet run --project Tools/UdonSharpLinterCLI/UdonSharpLinterCLI.csproj --generate-readme
```

Or using mise:

```bash
mise run generate-readme
```
