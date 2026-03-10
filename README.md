# UdonSharpLinterCLI

[![NuGet](https://img.shields.io/nuget/v/tktco.UdonSharpLinter)](https://www.nuget.org/packages/tktco.UdonSharpLinter)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

🌐 **Language:** English | [日本語](README.ja.md)

A static code analyzer for UdonSharp scripts in VRChat projects. Detects language features and patterns not supported by UdonSharp at compile time.

---

## Installation

> **Requirements:** .NET 6.0 or later

```bash
dotnet tool install -g tktco.UdonSharpLinter
```

### Update

```bash
dotnet tool update -g tktco.UdonSharpLinter
```

---

## Quick Start

```bash
# Analyze all UdonSharp scripts in Assets
udonsharp-lint Assets

# Analyze excluding test scripts
udonsharp-lint Assets --exclude-test-scripts
```

---

## Usage

```
udonsharp-lint <directory_path> [--exclude-test-scripts]
```

| Argument / Option | Description |
|---|---|
| `<directory_path>` | Path to the directory to analyze |
| `--exclude-test-scripts` | Exclude `TestScripts` / `Tests` / `Test` directories |

### Output Format

Errors and warnings are reported in standard compiler format, compatible with most IDEs and CI/CD tools.

```
path/to/file.cs(line,column): error UDON###: Error message
path/to/file.cs(line,column): warning UDON###: Warning message
```

### Exit Codes

| Code | Meaning |
|---|---|
| `0` | No errors (warnings may be present) |
| `1` | Errors found or execution failed |

---

## Checks

### Basic Language Features

| Error Code | Check |
|---|---|
| UDON029 | Async Await |
| UDON008 | Collection initializers |
| UDON005 | Constructors |
| UDON018 | Generic classes |
| UDON006 | Generic methods |
| UDON030 | Goto statements |
| UDON003 | Local functions |
| UDON009 | Multidimensional arrays |
| UDON012 | Nested types |
| UDON027 | Null Conditional Operators |
| UDON007 | Object initializers |
| UDON011 | Static fields |
| UDON002 | Throw statements |
| UDON001 | Try Catch statements |

### API and Attribute Restrictions

| Error Code | Check |
|---|---|
| UDON019 | General Unexposed APIs |
| UDON017 | Interfaces |
| UDON016 | Method Overloads |
| UDON013 | Network Callable methods |
| UDON026 | Send Custom Event methods |
| UDON014 | Text Mesh Pro APIs |

### Cross-file and Semantic Analysis

| Error Code | Check |
|---|---|
| UDON020 | Cross File Field Access |
| UDON022 | Cross File Method Invocation |
| UDON021 | Static Method Field Access |
| UDON025 | Udon Behaviour Serializable Class Usage |

---

## Integration

### Visual Studio Code (`tasks.json`)

Integrates with the Problems panel and runs with Ctrl+Shift+B.

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

### mise (`mise.toml`)

```toml
[tasks.lint-udon]
run = "udonsharp-lint Assets"
```

---

## License

[MIT](LICENSE)
