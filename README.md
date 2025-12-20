# VRChat UdonSharp Linter

A static analyzer and linter for VRChat UdonSharp scripts that detects common errors and unsupported features at build time.

## Installation

Install as a global .NET tool:

```bash
dotnet tool install -g VRChat.UdonSharp.Linter
```

## Usage

```bash
# Lint all UdonSharp scripts in a directory
udonsharp-lint ./Assets/UdonSharp

# Exclude test scripts
udonsharp-lint ./Assets/UdonSharp --exclude-test-scripts
```

## Features

Detects 20+ UdonSharp restrictions including:
- Try/Catch/Finally statements
- Throw statements
- Local functions
- Object/Collection initializers
- Multidimensional arrays
- Constructors
- Generic methods/classes
- Static fields (except const)
- Nested types
- Properties (except with FieldChangeCallback)
- Method overloads
- Interface implementations
- Cross-file field access on custom serializable classes
- Cross-file method invocations on custom serializable classes
- TextMeshPro unexposed APIs (warnings)
- Reflection, Threading, File I/O, and Network APIs
- And more...

## Error Codes

The linter uses error codes in the format `UDONXXX`:

- `UDON001`: Try/Catch/Finally statements
- `UDON002`: Throw statements
- `UDON003`: Local functions
- `UDON005`: Constructors
- `UDON006`: Generic methods
- `UDON007`: Object initializers
- `UDON008`: Collection initializers
- `UDON009`: Multidimensional arrays
- `UDON011`: Static fields
- `UDON012`: Nested types
- `UDON013`: NetworkCallable method restrictions
- `UDON014`: TextMeshPro API (warning)
- `UDON015`: Properties
- `UDON016`: Method overloads
- `UDON017`: Interface implementation
- `UDON018`: Generic classes
- `UDON019`: Unexposed APIs
- `UDON020`: Cross-file field access
- `UDON021`: Static method field access
- `UDON022`: Cross-file method invocation
- `UDON025`: System.Serializable class usage

## Exit Codes

- `0`: No errors found
- `1`: Errors detected or invalid usage

## CI/CD Integration

### GitHub Actions

```yaml
- name: Install UdonSharp Linter
  run: dotnet tool install -g VRChat.UdonSharp.Linter

- name: Run Linter
  run: udonsharp-lint ./Assets/Scripts
```

## License

MIT
