using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace tktco.UdonSharpLinter
{
    /// <summary>
    /// README.md自動生成ツール
    /// Program.csのドキュメントコメントとエラーコード定義からREADMEを生成
    /// </summary>
    class READMEGenerator
    {
        private class ErrorCodeInfo
        {
            public int Code { get; set; }
            public string Name { get; set; } = "";
            public string Comment { get; set; } = "";
        }

        private class CheckMethodInfo
        {
            public string MethodName { get; set; } = "";
            public string Summary { get; set; } = "";
            public string Description { get; set; } = "";
            public List<int> ErrorCodes { get; set; } = new List<int>();
        }

        public static void Generate()
        {
            var programCsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Program.cs");
            programCsPath = Path.GetFullPath(programCsPath);

            if (!File.Exists(programCsPath))
            {
                Console.Error.WriteLine($"Error: Program.cs not found at {programCsPath}");
                Environment.Exit(1);
            }

            // プロジェクトルート（Tools/UdonSharpLinter/）に README を生成
            var projectRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(programCsPath)!, "..", ".."));
            var readmePath = Path.Combine(projectRoot, "README.md");

            Console.WriteLine($"[README Generator] Analyzing {programCsPath}...");

            var sourceCode = File.ReadAllText(programCsPath);
            var tree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = tree.GetRoot();

            // エラーコード定義を抽出
            var errorCodes = ExtractErrorCodes(root);

            // チェックメソッド情報を抽出
            var checkMethods = ExtractCheckMethods(root);

            // カテゴリ別に分類
            var categorizedMethods = CategorizeCheckMethods(checkMethods, errorCodes);

            // READMEを生成
            var readme = GenerateREADME(errorCodes, categorizedMethods);

            File.WriteAllText(readmePath, readme);
            Console.WriteLine($"[README Generator] Generated {readmePath}");
        }

        private static Dictionary<int, ErrorCodeInfo> ExtractErrorCodes(SyntaxNode root)
        {
            var errorCodes = new Dictionary<int, ErrorCodeInfo>();

            var errorCodeClass = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == "LintErrorCodes");

            if (errorCodeClass == null)
                return errorCodes;

            // クラスのドキュメントコメントを取得
            var classTrivia = errorCodeClass.GetLeadingTrivia()
                .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                           t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                .ToList();

            var fields = errorCodeClass.Members.OfType<FieldDeclarationSyntax>();

            foreach (var field in fields)
            {
                if (field.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword)))
                {
                    var variable = field.Declaration.Variables.FirstOrDefault();
                    if (variable == null) continue;

                    var name = variable.Identifier.Text;
                    var initializer = variable.Initializer?.Value?.ToString();

                    if (int.TryParse(initializer, out var code))
                    {
                        // フィールドのトレーリングコメントを取得
                        var comment = ExtractTrailingComment(field);

                        errorCodes[code] = new ErrorCodeInfo
                        {
                            Code = code,
                            Name = name,
                            Comment = comment
                        };
                    }
                }
            }

            return errorCodes;
        }

        private static string ExtractTrailingComment(SyntaxNode node)
        {
            var trivia = node.GetTrailingTrivia()
                .Concat(node.Parent?.GetTrailingTrivia() ?? Enumerable.Empty<SyntaxTrivia>());

            var comment = trivia
                .Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                           t.IsKind(SyntaxKind.MultiLineCommentTrivia))
                .Select(t => t.ToString().TrimStart('/', '*', ' ').TrimEnd('*', '/', ' '))
                .FirstOrDefault();

            return comment ?? "";
        }

        private static List<CheckMethodInfo> ExtractCheckMethods(SyntaxNode root)
        {
            var methods = new List<CheckMethodInfo>();

            var checkMethods = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.Text.StartsWith("Check"));

            foreach (var method in checkMethods)
            {
                var docComment = ExtractDocComment(method);
                var errorCodes = ExtractErrorCodesFromMethod(method);

                if (!string.IsNullOrEmpty(docComment))
                {
                    var (summary, description) = ParseDocComment(docComment);

                    methods.Add(new CheckMethodInfo
                    {
                        MethodName = method.Identifier.Text,
                        Summary = summary,
                        Description = description,
                        ErrorCodes = errorCodes
                    });
                }
            }

            return methods;
        }

        private static string ExtractDocComment(MethodDeclarationSyntax method)
        {
            var trivia = method.GetLeadingTrivia()
                .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                           t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                .ToList();

            if (!trivia.Any())
                return "";

            var fullComment = string.Join("\n", trivia.Select(t => t.ToString()));

            // XML タグを除去してテキストのみ抽出
            var lines = fullComment.Split('\n')
                .Select(line => line.TrimStart('/', ' ', '\t'))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            return string.Join("\n", lines);
        }

        private static (string summary, string description) ParseDocComment(string docComment)
        {
            var lines = docComment.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            // <summary>タグを探す
            var summaryMatch = Regex.Match(docComment, @"<summary>\s*(.+?)\s*</summary>", RegexOptions.Singleline);
            var summary = summaryMatch.Success
                ? summaryMatch.Groups[1].Value
                : lines.FirstOrDefault() ?? "";

            // XMLタグを除去
            summary = Regex.Replace(summary, @"<[^>]+>", "");

            // 最初の一文だけを取得（改行または「。」まで）
            var firstSentence = Regex.Match(summary, @"^(.+?)[。\r\n]").Groups[1].Value;
            if (!string.IsNullOrEmpty(firstSentence))
            {
                summary = firstSentence.Trim();
            }

            // HTMLエンティティをデコード
            summary = System.Net.WebUtility.HtmlDecode(summary);

            // 複数の空白を1つに
            summary = Regex.Replace(summary, @"\s+", " ").Trim();

            // summaryタグ以外の部分を説明として取得
            var description = Regex.Replace(docComment, @"<summary>.*?</summary>", "", RegexOptions.Singleline)
                .Trim();

            // XMLタグを除去
            description = Regex.Replace(description, @"<[^>]+>", "");

            // 複数の空白を1つに
            description = Regex.Replace(description, @"\s+", " ").Trim();

            return (summary, description);
        }

        private static List<int> ExtractErrorCodesFromMethod(MethodDeclarationSyntax method)
        {
            var errorCodes = new List<int>();

            // LintErrorCodes.XXX の参照を探す
            var memberAccesses = method.DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Where(m => m.Expression.ToString() == "LintErrorCodes");

            foreach (var access in memberAccesses)
            {
                var memberName = access.Name.ToString();
                // コード名からコード番号を推測（実際の値は ExtractErrorCodes で取得済み）
                errorCodes.Add(0); // プレースホルダー
            }

            return errorCodes.Distinct().ToList();
        }

        private static Dictionary<string, List<CheckMethodInfo>> CategorizeCheckMethods(
            List<CheckMethodInfo> methods,
            Dictionary<int, ErrorCodeInfo> errorCodes)
        {
            var categories = new Dictionary<string, List<CheckMethodInfo>>
            {
                { "Basic Language Features", new List<CheckMethodInfo>() },
                { "API and Attribute Restrictions", new List<CheckMethodInfo>() },
                { "Cross-file and Semantic Analysis", new List<CheckMethodInfo>() }
            };

            // エラーコードの範囲でカテゴリ分け
            var basicFeatures = new[] { 1, 2, 3, 5, 6, 7, 8, 9, 11, 12, 18 };
            var apiRestrictions = new[] { 13, 14, 15, 16, 17, 19 };
            var semanticAnalysis = new[] { 20, 21, 22, 25 };

            foreach (var method in methods)
            {
                var methodErrorCodes = ExtractErrorCodeNumbers(method.MethodName, errorCodes);

                if (methodErrorCodes.Any(c => basicFeatures.Contains(c)))
                {
                    categories["Basic Language Features"].Add(method);
                }
                else if (methodErrorCodes.Any(c => apiRestrictions.Contains(c)))
                {
                    categories["API and Attribute Restrictions"].Add(method);
                }
                else if (methodErrorCodes.Any(c => semanticAnalysis.Contains(c)))
                {
                    categories["Cross-file and Semantic Analysis"].Add(method);
                }
            }

            return categories;
        }

        private static string ConvertMethodNameToDescription(string methodName)
        {
            // CheckXXX -> XXX
            var baseName = methodName.Replace("Check", "");

            // キャメルケースをスペース区切りに変換
            var description = Regex.Replace(baseName, "([a-z])([A-Z])", "$1 $2");

            // 末尾の "Statements", "Methods" などを調整
            description = description
                .Replace("Statements", "statements")
                .Replace("Methods", "methods")
                .Replace("Fields", "fields")
                .Replace("Types", "types")
                .Replace("Classes", "classes")
                .Replace("APIs", "APIs")
                .Replace("Initializers", "initializers")
                .Replace("Arrays", "arrays")
                .Replace("Functions", "functions");

            return description;
        }

        private static List<int> ExtractErrorCodeNumbers(string methodName, Dictionary<int, ErrorCodeInfo> errorCodes)
        {
            // メソッド名からエラーコード名を推測
            // CheckTryCatchStatements -> TryCatch -> errorCodes.Values.First(e => e.Name == "TryCatch").Code

            var methodBaseName = methodName.Replace("Check", "");
            methodBaseName = Regex.Replace(methodBaseName, "(Statements|Methods|Fields|Types|Classes|APIs|Initializers|Arrays)", "");
            methodBaseName = methodBaseName.Trim();

            var matchingCodes = errorCodes.Values
                .Where(e => e.Name.Contains(methodBaseName, StringComparison.OrdinalIgnoreCase) ||
                           methodBaseName.Contains(e.Name, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Code)
                .ToList();

            return matchingCodes;
        }

        private static string GenerateREADME(
            Dictionary<int, ErrorCodeInfo> errorCodes,
            Dictionary<string, List<CheckMethodInfo>> categorizedMethods)
        {
            var sb = new StringBuilder();

            // ヘッダー
            sb.AppendLine("# UdonSharpLinterCLI");
            sb.AppendLine();
            sb.AppendLine("A static code analyzer for UdonSharp scripts in VRChat projects. This tool detects language features and patterns that are not supported by UdonSharp at compile time.");
            sb.AppendLine();

            // Features セクション
            sb.AppendLine("## Features");
            sb.AppendLine();
            sb.AppendLine("UdonSharpLinterCLI performs comprehensive checks for UdonSharp restrictions, including:");
            sb.AppendLine();

            foreach (var category in categorizedMethods)
            {
                if (!category.Value.Any()) continue;

                sb.AppendLine($"### {category.Key}");

                foreach (var method in category.Value.OrderBy(m => m.MethodName))
                {
                    var codes = ExtractErrorCodeNumbers(method.MethodName, errorCodes);
                    var codeStr = codes.Any() ? $" (UDON{string.Join(", UDON", codes.Select(c => c.ToString("D3")))})" : "";

                    // 英語の説明を生成
                    var englishDescription = ConvertMethodNameToDescription(method.MethodName);

                    sb.AppendLine($"- {englishDescription}{codeStr}");
                }

                sb.AppendLine();
            }

            // 静的な部分（Usage以降）
            sb.AppendLine(@"## Usage

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
  ""version"": ""2.0.0"",
  ""tasks"": [
    {
      ""label"": ""UdonSharp Lint"",
      ""type"": ""shell"",
      ""command"": ""dotnet"",
      ""args"": [
        ""run"",
        ""--project"",
        ""Tools/UdonSharpLinterCLI/UdonSharpLinterCLI.csproj"",
        ""--"",
        ""${workspaceFolder}/Assets""
      ],
      ""problemMatcher"": {
        ""owner"": ""udonsharp"",
        ""fileLocation"": [""relative"", ""${workspaceFolder}""],
        ""pattern"": {
          ""regexp"": ""^(.+)\\((\\d+),(\\d+)\\):\\s+(error|warning)\\s+UDON(\\d+):\\s+(.+)$"",
          ""file"": 1,
          ""line"": 2,
          ""column"": 3,
          ""severity"": 4,
          ""code"": 5,
          ""message"": 6
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
[tasks.""lint-udon""]
run = ""dotnet run --project Tools/UdonSharpLinterCLI/UdonSharpLinterCLI.csproj -- Assets""
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
```");

            return sb.ToString();
        }
    }
}
