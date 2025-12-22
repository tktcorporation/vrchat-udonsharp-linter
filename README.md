# UdonSharpLinterCLI

A static code analyzer for UdonSharp scripts in VRChat projects. This tool detects language features and patterns that are not supported by UdonSharp at compile time.

## Features

UdonSharpLinterCLI performs comprehensive checks for UdonSharp restrictions, including:

### Basic Language Features
- Async Await (UDON029)
- Collection initializers (UDON008)
- Constructors (UDON005)
- Generic classes (UDON018)
- Generic methods (UDON006)
- Goto statements (UDON030)
- Local functions (UDON003)
- Multidimensional arrays (UDON009)
- Nested types (UDON012)
- Null Coalescing Operators (UDON028)
- Null Conditional Operators (UDON027)
- Object initializers (UDON007)
- Static fields (UDON011)
- Throw statements (UDON002)
- Try Catch statements (UDON001)

### API and Attribute Restrictions
- General Unexposed APIs (UDON019)
- Interfaces (UDON017)
- Method Overloads (UDON016)
- Network Callable methods (UDON013)
- Properties (UDON015)
- Send Custom Event methods (UDON026)
- Text Mesh Pro APIs (UDON014)

### Cross-file and Semantic Analysis
- Cross File Field Access (UDON020)
- Cross File Method Invocation (UDON022)
- Static Method Field Access (UDON021)
- Udon Behaviour Serializable Class Usage (UDON025)

## Installation

```bash
dotnet tool install -g tktco.UdonSharpLinter
```

## Usage

```bash
udonsharp-lint <directory_path> [--exclude-test-scripts]
```

### Arguments

- `<directory_path>`: Path to the directory containing UdonSharp scripts to analyze
- `--exclude-test-scripts`: (Optional) Exclude scripts in TestScripts, Tests, or Test directories

### Examples

```bash
# Analyze all UdonSharp scripts in Assets
udonsharp-lint Assets

# Analyze excluding test scripts
udonsharp-lint Assets --exclude-test-scripts
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

## Integration Examples

### Visual Studio Code (tasks.json)

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "UdonSharp Lint",
      "type": "shell",
      "command": "udonsharp-lint",
      "args": ["${workspaceFolder}/Assets"],
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
- name: Install UdonSharp Linter
  run: dotnet tool install -g tktco.UdonSharpLinter

- name: Run UdonSharp Linter
  run: udonsharp-lint Assets --exclude-test-scripts
```

### mise (mise.toml)

```toml
[tasks.lint-udon]
run = "udonsharp-lint Assets"
```
