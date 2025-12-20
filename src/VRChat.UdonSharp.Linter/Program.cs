using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpLinterCLI
{
    class Program
    {
        #region Fields

        private static int _errorCount = 0;
        private static int _warningCount = 0;
        private static bool _hasErrors = false;
        private static readonly object _lockObject = new object();

        #endregion

        #region Main Entry Point

        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: udonsharp-lint <directory_path> [--exclude-test-scripts]");
                return 1;
            }

            string directoryPath = args[0];
            if (!Directory.Exists(directoryPath))
            {
                Console.Error.WriteLine($"Error: Directory '{directoryPath}' does not exist.");
                return 1;
            }

            bool excludeTestScripts = args.Length > 1 && args[1] == "--exclude-test-scripts";

            Console.WriteLine($"[UdonSharp Linter] Scanning directory: {directoryPath}");

            var csFiles = Directory.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("\\Temp\\") && !f.Contains("\\Library\\") && !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
                .Where(f => !f.Contains("\\Editor\\") && !f.Contains("\\editor\\")) // Exclude Editor scripts
                .Where(f => !excludeTestScripts || (!f.Contains("\\TestScripts\\") && !f.Contains("\\Tests\\") && !f.Contains("\\Test\\"))) // Optionally exclude test scripts
                .ToList();

            // Filter and process files in parallel
            var udonSharpFiles = new ConcurrentBag<string>();
            Parallel.ForEach(csFiles, file =>
            {
                try
                {
                    string content = File.ReadAllText(file);
                    if (content.Contains("UdonSharpBehaviour") && content.Contains("using UdonSharp;"))
                    {
                        udonSharpFiles.Add(file);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Could not read file {file}: {ex.Message}");
                }
            });

            var filteredFiles = udonSharpFiles.ToList();

            if (!filteredFiles.Any())
            {
                Console.WriteLine("[UdonSharp Linter] No UdonSharp scripts found.");
                return 0;
            }

            Console.WriteLine($"[UdonSharp Linter] Found {filteredFiles.Count} UdonSharp scripts to check.");

            // 全C#ファイルの構文木を構築（セマンティック解析のため、UdonSharpBehaviourを含まないファイルも含める）
            var syntaxTreeDict = new ConcurrentDictionary<string, SyntaxTree>();
            Parallel.ForEach(csFiles, file =>
            {
                try
                {
                    string content = File.ReadAllText(file);
                    var tree = CSharpSyntaxTree.ParseText(content, path: file);
                    // キーを正規化してフルパスに統一
                    var normalizedPath = Path.GetFullPath(file);
                    syntaxTreeDict[normalizedPath] = tree;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Could not parse file {file}: {ex.Message}");
                }
            });

            // コンパイル情報を構築
            var compilation = CreateCompilation(syntaxTreeDict.Values.ToList());

            // UdonSharpスクリプトから呼び出される静的メソッドのコールグラフを構築
            var callGraph = BuildCallGraph(compilation, filteredFiles);

            // 各ファイルをlint
            Parallel.ForEach(filteredFiles, file =>
            {
                var normalizedPath = Path.GetFullPath(file);
                if (syntaxTreeDict.TryGetValue(normalizedPath, out var tree))
                {
                    LintFile(file, tree, compilation, callGraph);
                }
            });

            // 静的メソッドを含むファイルもチェック（UdonSharpから呼び出される場合）
            foreach (var entry in callGraph)
            {
                var staticMethodFile = entry.Key;
                var callingFiles = entry.Value;

                if (syntaxTreeDict.TryGetValue(staticMethodFile, out var tree))
                {
                    LintStaticMethodFile(staticMethodFile, tree, compilation, callingFiles);
                }
            }

            Console.WriteLine($"\n[UdonSharp Linter] Summary: {_errorCount} errors, {_warningCount} warnings");

            return _hasErrors ? 1 : 0;
        }

        /// <summary>
        /// コンパイル情報を構築
        /// セマンティック解析に必要な型情報を提供
        /// </summary>
        private static CSharpCompilation CreateCompilation(List<SyntaxTree> syntaxTrees)
        {
            // 基本的な参照アセンブリを追加
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            };

            // Unity/UdonSharp参照アセンブリを追加（存在する場合）
            try
            {
                // UnityEngine.dll
                var unityEnginePath = Path.Combine(Directory.GetCurrentDirectory(), "Library", "ScriptAssemblies", "UnityEngine.dll");
                if (File.Exists(unityEnginePath))
                {
                    references.Add(MetadataReference.CreateFromFile(unityEnginePath));
                }

                // VRChat SDK
                var vrcSdkPath = Path.Combine(Directory.GetCurrentDirectory(), "Library", "ScriptAssemblies", "VRC.SDKBase.dll");
                if (File.Exists(vrcSdkPath))
                {
                    references.Add(MetadataReference.CreateFromFile(vrcSdkPath));
                }

                // UdonSharp Runtime
                var udonSharpPath = Path.Combine(Directory.GetCurrentDirectory(), "Library", "ScriptAssemblies", "UdonSharp.Runtime.dll");
                if (File.Exists(udonSharpPath))
                {
                    references.Add(MetadataReference.CreateFromFile(udonSharpPath));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] Could not load Unity assemblies: {ex.Message}");
            }

            return CSharpCompilation.Create(
                "UdonSharpLinter",
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
        }

        private static void LintFile(string filePath, SyntaxTree tree, CSharpCompilation compilation, Dictionary<string, HashSet<string>> callGraph)
        {
            try
            {
                var root = tree.GetRoot();

                var errors = new List<LintError>();

                // Check for various UdonSharp restrictions
                CheckTryCatchStatements(root, filePath, errors);
                CheckThrowStatements(root, filePath, errors);
                CheckLocalFunctions(root, filePath, errors);
                CheckObjectInitializers(root, filePath, errors);
                CheckCollectionInitializers(root, filePath, errors);
                CheckMultidimensionalArrays(root, filePath, errors);
                CheckConstructors(root, filePath, errors);
                CheckGenericMethods(root, filePath, errors);
                CheckGenericClasses(root, filePath, errors);
                CheckStaticFields(root, filePath, errors);
                CheckNestedTypes(root, filePath, errors);
                CheckNetworkCallableMethods(root, filePath, errors);
                CheckTextMeshProAPIs(root, filePath, errors);
                CheckGeneralUnexposedAPIs(root, filePath, errors);
                CheckProperties(root, filePath, errors);
                CheckMethodOverloads(root, filePath, errors);
                CheckInterfaces(root, filePath, errors);
                CheckCrossFileFieldAccess(root, filePath, errors, compilation);
                CheckCrossFileMethodInvocation(root, filePath, errors, compilation);
                CheckUdonBehaviourSerializableClassUsage(root, filePath, errors, compilation);

                // Report errors
                foreach (var error in errors)
                {
                    string severityPrefix = error.Severity == DiagnosticSeverity.Error ? "error" : "warning";
                    string relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), error.FilePath).Replace('\\', '/');

                    lock (_lockObject)
                    {
                        Console.WriteLine($"{relativePath}({error.Line},{error.Column}): {severityPrefix} UDON{error.Code:D3}: {error.Message}");

                        if (error.Severity == DiagnosticSeverity.Error)
                        {
                            _errorCount++;
                            _hasErrors = true;
                        }
                        else
                        {
                            _warningCount++;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error processing file {filePath}: {e.Message}");
                _hasErrors = true;
            }
        }

        /// <summary>
        /// 静的メソッドを含むファイル（UdonSharpから呼び出される）のlint
        /// </summary>
        private static void LintStaticMethodFile(string filePath, SyntaxTree tree, CSharpCompilation compilation, HashSet<string> callingUdonSharpFiles)
        {
            try
            {
                var root = tree.GetRoot();
                var errors = new List<LintError>();

                // 静的メソッド内のフィールドアクセスをチェック
                CheckStaticMethodFieldAccess(root, filePath, errors, compilation, callingUdonSharpFiles);

                // Report errors
                foreach (var error in errors)
                {
                    string severityPrefix = error.Severity == DiagnosticSeverity.Error ? "error" : "warning";
                    string relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), error.FilePath).Replace('\\', '/');

                    lock (_lockObject)
                    {
                        Console.WriteLine($"{relativePath}({error.Line},{error.Column}): {severityPrefix} UDON{error.Code:D3}: {error.Message}");

                        if (error.Severity == DiagnosticSeverity.Error)
                        {
                            _errorCount++;
                            _hasErrors = true;
                        }
                        else
                        {
                            _warningCount++;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error processing file {filePath}: {e.Message}");
                _hasErrors = true;
            }
        }

        #endregion

        #region Models

        private class LintError
        {
            public string FilePath { get; set; } = "";
            public int Line { get; set; }
            public int Column { get; set; }
            public string Message { get; set; } = "";
            public DiagnosticSeverity Severity { get; set; }
            public int Code { get; set; }
        }

        /// <summary>
        /// Lint error code constants
        /// Note: Some numbers are skipped (reserved for future use or removed checks)
        /// - 4, 10: Reserved for future use
        /// - 23, 24: Removed (replaced by UDON025)
        /// </summary>
        private static class LintErrorCodes
        {
            // Basic language feature restrictions
            public const int TryCatch = 1;
            public const int Throw = 2;
            public const int LocalFunction = 3;
            public const int Constructor = 5;
            public const int GenericMethod = 6;
            public const int ObjectInitializer = 7;
            public const int CollectionInitializer = 8;
            public const int MultidimensionalArray = 9;
            public const int StaticField = 11;
            public const int NestedType = 12;
            public const int GenericClass = 18;

            // API and attribute restrictions
            public const int NetworkCallable = 13;
            public const int TextMeshProAPI = 14;
            public const int UnexposedAPI = 19;
            public const int Property = 15;
            public const int MethodOverload = 16;
            public const int Interface = 17;

            // Cross-file and semantic analysis
            public const int CrossFileFieldAccess = 20;
            public const int StaticMethodFieldAccess = 21;
            public const int CrossFileMethodInvocation = 22;
            public const int UdonBehaviourSerializableClassUsage = 25;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Helper method to add a lint error to the error list
        /// </summary>
        private static void AddError(
            List<LintError> errors,
            string filePath,
            SyntaxNode node,
            string message,
            int code,
            DiagnosticSeverity severity = DiagnosticSeverity.Error)
        {
            var lineSpan = node.GetLocation().GetLineSpan();
            errors.Add(new LintError
            {
                FilePath = filePath,
                Line = lineSpan.StartLinePosition.Line + 1,
                Column = lineSpan.StartLinePosition.Character + 1,
                Message = message,
                Severity = severity,
                Code = code
            });
        }

        /// <summary>
        /// Helper method to find syntax nodes of a specific type
        /// </summary>
        private static IEnumerable<T> FindNodes<T>(SyntaxNode root) where T : SyntaxNode
        {
            return root.DescendantNodes().OfType<T>();
        }

        /// <summary>
        /// Helper method to find UdonSharpBehaviour classes
        /// </summary>
        private static IEnumerable<ClassDeclarationSyntax> FindUdonSharpBehaviourClasses(SyntaxNode root)
        {
            return FindNodes<ClassDeclarationSyntax>(root)
                .Where(c => IsUdonSharpBehaviourClass(c));
        }

        /// <summary>
        /// Helper method to check if a member has a specific attribute
        /// </summary>
        private static bool HasAttribute(MemberDeclarationSyntax member, string attributeName)
        {
            return member.AttributeLists.Any(al =>
                al.Attributes.Any(a => a.Name.ToString().Contains(attributeName)));
        }

        #endregion

        #region Syntax Checks

        /// <summary>
        /// UdonSharp制約: Try/Catch/Finally文は使用できません
        ///
        /// Udonでは例外処理機構がサポートされていないため、try-catch-finally構文は使用できません。
        /// エラーハンドリングは、戻り値やフラグを使った明示的なチェックで行う必要があります。
        ///
        /// 例:
        /// NG: try { DoSomething(); } catch (Exception e) { }
        /// OK: if (!DoSomething()) { /* エラー処理 */ }
        /// </summary>
        private static void CheckTryCatchStatements(SyntaxNode root, string filePath, List<LintError> errors)
        {
            var tryStatements = root.DescendantNodes().OfType<TryStatementSyntax>();
            foreach (var tryStatement in tryStatements)
            {
                AddError(errors, filePath, tryStatement,
                    "Try/Catch/Finally statements are not supported in UdonSharp", LintErrorCodes.TryCatch);
            }
        }

        /// <summary>
        /// UdonSharp制約: Throw文は使用できません
        ///
        /// Udonでは例外のスローがサポートされていないため、throw文やthrow式は使用できません。
        /// エラー状態の伝達は、戻り値、out/refパラメータ、またはクラスのフィールドを使用します。
        ///
        /// 例:
        /// NG: throw new ArgumentException("Invalid");
        /// NG: var result = condition ? value : throw new Exception();
        /// OK: return false; // エラー時はfalseを返す
        /// </summary>
        private static void CheckThrowStatements(SyntaxNode root, string filePath, List<LintError> errors)
        {
            var throwStatements = root.DescendantNodes()
                .Where(n => n is ThrowStatementSyntax || n is ThrowExpressionSyntax);

            foreach (var throwStatement in throwStatements)
            {
                AddError(errors, filePath, throwStatement,
                    "Throw statements are not supported in UdonSharp", LintErrorCodes.Throw);
            }
        }

        /// <summary>
        /// UdonSharp制約: ローカル関数は使用できません
        ///
        /// Udonではローカル関数（メソッド内で定義される関数）がサポートされていません。
        /// 代わりに、クラスのprivateメソッドとして定義する必要があります。
        ///
        /// 例:
        /// NG: void MyMethod() { void LocalFunc() { } }
        /// OK: void MyMethod() { Helper(); } private void Helper() { }
        /// </summary>
        private static void CheckLocalFunctions(SyntaxNode root, string filePath, List<LintError> errors)
        {
            var localFunctions = root.DescendantNodes().OfType<LocalFunctionStatementSyntax>();
            foreach (var localFunction in localFunctions)
            {
                AddError(errors, filePath, localFunction,
                    "Local functions are not supported in UdonSharp", LintErrorCodes.LocalFunction);
            }
        }

        /// <summary>
        /// UdonSharp制約: オブジェクト初期化子は使用できません
        ///
        /// Udonではオブジェクト初期化子（{ }構文でプロパティを初期化）がサポートされていません。
        /// オブジェクトの初期化は、コンストラクタや個別のプロパティ設定で行う必要があります。
        ///
        /// 例:
        /// NG: var obj = new MyClass { X = 1, Y = 2 };
        /// OK: var obj = new MyClass(); obj.X = 1; obj.Y = 2;
        /// </summary>
        private static void CheckObjectInitializers(SyntaxNode root, string filePath, List<LintError> errors)
        {
            // Check for object initializers (e.g., new MyClass { X = 1 })
            var objectCreations = root.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Where(n => n.Initializer != null &&
                           n.Initializer.Kind() == SyntaxKind.ObjectInitializerExpression);

            foreach (var initializer in objectCreations)
            {
                AddError(errors, filePath, initializer,
                    "Object initializers are not supported in UdonSharp", LintErrorCodes.ObjectInitializer);
            }
        }

        /// <summary>
        /// UdonSharp制約: コレクション初期化子は使用できません
        ///
        /// Udonではコレクション初期化子がサポートされていません。
        /// ただし、配列初期化子（new int[] { 1, 2, 3 }）は使用可能です。
        ///
        /// 例:
        /// NG: var list = new List&lt;int&gt; { 1, 2, 3 };
        /// OK: var list = new List&lt;int&gt;(); list.Add(1); list.Add(2);
        /// OK: var arr = new int[] { 1, 2, 3 }; // 配列初期化子はOK
        /// </summary>
        private static void CheckCollectionInitializers(SyntaxNode root, string filePath, List<LintError> errors)
        {
            // Check for collection initializers on non-array types
            var collectionCreations = root.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Where(n => n.Initializer != null &&
                           n.Initializer.Kind() == SyntaxKind.CollectionInitializerExpression &&
                           !(n.Type is ArrayTypeSyntax));

            foreach (var initializer in collectionCreations)
            {
                AddError(errors, filePath, initializer,
                    "Collection initializers are not supported in UdonSharp (array initializers are allowed)", LintErrorCodes.CollectionInitializer);
            }
        }

        /// <summary>
        /// UdonSharp制約: 多次元配列は使用できません
        ///
        /// Udonでは多次元配列（int[,]やint[,,]など）がサポートされていません。
        /// 代わりに、ジャグ配列（配列の配列）を使用する必要があります。
        ///
        /// 例:
        /// NG: int[,] matrix = new int[3, 3];
        /// NG: int[,,] cube = new int[2, 2, 2];
        /// OK: int[][] jaggedArray = new int[3][];
        /// OK: jaggedArray[0] = new int[3];
        /// </summary>
        private static void CheckMultidimensionalArrays(SyntaxNode root, string filePath, List<LintError> errors)
        {
            var arrayTypes = root.DescendantNodes().OfType<ArrayTypeSyntax>();
            foreach (var arrayType in arrayTypes)
            {
                if (arrayType.RankSpecifiers.Any(rs => rs.Sizes.Count > 1))
                {
                    AddError(errors, filePath, arrayType,
                        "Multidimensional arrays are not supported in UdonSharp. Use jagged arrays instead", LintErrorCodes.MultidimensionalArray);
                }
            }
        }

        /// <summary>
        /// UdonSharp制約: コンストラクタは使用できません
        ///
        /// UdonSharpBehaviourを継承したクラスでは、コンストラクタを定義できません。
        /// 初期化処理は、Unityのライフサイクルメソッド（Start、Awake等）で行う必要があります。
        /// これは、UdonSharpのオブジェクト生成がUnityのコンポーネントシステムに依存しているためです。
        ///
        /// 例:
        /// NG: public MyBehaviour() { initialized = true; }
        /// OK: void Start() { initialized = true; }
        /// </summary>
        private static void CheckConstructors(SyntaxNode root, string filePath, List<LintError> errors)
        {
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(c => IsUdonSharpBehaviourClass(c));

            foreach (var classDecl in classes)
            {
                var constructors = classDecl.Members.OfType<ConstructorDeclarationSyntax>();
                foreach (var constructor in constructors)
                {
                    AddError(errors, filePath, constructor,
                        "Constructors are not supported in UdonSharpBehaviour", LintErrorCodes.Constructor);
                }
            }
        }

        /// <summary>
        /// UdonSharp制約: ジェネリックメソッドは使用できません
        ///
        /// UdonSharpBehaviourでは、ジェネリック型パラメータを持つメソッドを定義できません。
        /// 型安全性が必要な場合は、具体的な型でメソッドをオーバーロードするか、
        /// object型を使用してキャストする必要があります。
        ///
        /// 例:
        /// NG: public T GetValue&lt;T&gt;() { }
        /// OK: public int GetIntValue() { }
        /// OK: public string GetStringValue() { }
        /// </summary>
        private static void CheckGenericMethods(SyntaxNode root, string filePath, List<LintError> errors)
        {
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(c => IsUdonSharpBehaviourClass(c));

            foreach (var classDecl in classes)
            {
                var genericMethods = classDecl.Members.OfType<MethodDeclarationSyntax>()
                    .Where(m => m.TypeParameterList != null);

                foreach (var method in genericMethods)
                {
                    AddError(errors, filePath, method,
                        "Generic methods are not supported in UdonSharpBehaviour", LintErrorCodes.GenericMethod);
                }
            }
        }

        /// <summary>
        /// UdonSharp制約: staticフィールドは使用できません（constは除く）
        ///
        /// UdonSharpBehaviourでは、staticフィールドを定義できません。
        /// これは、Udonの実行環境がインスタンスごとに独立しており、静的状態の共有がサポートされていないためです。
        /// ただし、const（コンパイル時定数）は使用可能です。
        ///
        /// 例:
        /// NG: public static int counter = 0;
        /// NG: private static string sharedData;
        /// OK: public const int MAX_COUNT = 100;
        /// OK: private const string PREFIX = "Player";
        /// </summary>
        private static void CheckStaticFields(SyntaxNode root, string filePath, List<LintError> errors)
        {
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(c => IsUdonSharpBehaviourClass(c));

            foreach (var classDecl in classes)
            {
                var staticFields = classDecl.Members.OfType<FieldDeclarationSyntax>()
                    .Where(f => f.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)) &&
                               !f.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword)));

                foreach (var field in staticFields)
                {
                    AddError(errors, filePath, field,
                        "Static fields are not supported in UdonSharpBehaviour (const is allowed)", LintErrorCodes.StaticField);
                }
            }
        }

        /// <summary>
        /// UdonSharp制約: ネストした型は使用できません
        ///
        /// UdonSharpBehaviourクラスの内部に、クラス、構造体、列挙型などを定義することはできません。
        /// すべての型は、トップレベル（名前空間直下）で定義する必要があります。
        ///
        /// 例:
        /// NG: class MyBehaviour : UdonSharpBehaviour { class Inner { } }
        /// NG: class MyBehaviour : UdonSharpBehaviour { enum State { } }
        /// OK: enum State { } class MyBehaviour : UdonSharpBehaviour { }
        /// </summary>
        private static void CheckNestedTypes(SyntaxNode root, string filePath, List<LintError> errors)
        {
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(c => IsUdonSharpBehaviourClass(c));

            foreach (var classDecl in classes)
            {
                var nestedTypes = classDecl.Members
                    .Where(m => m is TypeDeclarationSyntax);

                foreach (var nestedType in nestedTypes)
                {
                    AddError(errors, filePath, nestedType,
                        "Nested types are not supported in UdonSharpBehaviour", LintErrorCodes.NestedType);
                }
            }
        }

        /// <summary>
        /// UdonSharp制約: [NetworkCallable]属性付きメソッドには厳しい制約があります
        ///
        /// ネットワーク経由で呼び出し可能なメソッド（[NetworkCallable]属性付き）には以下の制約があります：
        /// - 戻り値はvoid型でなければならない
        /// - パラメータは最大8個まで
        /// - ref/outパラメータは使用できない
        /// - paramsキーワードは使用できない
        /// - デフォルト値付きパラメータは使用できない
        /// - static, abstract, virtual, override, sealed修飾子は使用できない
        ///
        /// これらの制約は、ネットワークを介した安全なデータ送信のために設けられています。
        ///
        /// 例:
        /// NG: [NetworkCallable] public int GetValue() { }
        /// NG: [NetworkCallable] public void Process(ref int value) { }
        /// OK: [NetworkCallable] public void SendData(int value, string message) { }
        /// </summary>
        private static void CheckNetworkCallableMethods(SyntaxNode root, string filePath, List<LintError> errors)
        {
            var methods = FindNodes<MethodDeclarationSyntax>(root)
                .Where(m => HasAttribute(m, "NetworkCallable"));

            foreach (var method in methods)
            {
                CheckNetworkCallableReturnType(method, filePath, errors);
                CheckNetworkCallableParameterCount(method, filePath, errors);
                CheckNetworkCallableRefOutParameters(method, filePath, errors);
                CheckNetworkCallableParamsKeyword(method, filePath, errors);
                CheckNetworkCallableDefaultValues(method, filePath, errors);
                CheckNetworkCallableModifiers(method, filePath, errors);
            }
        }

        private static void CheckNetworkCallableReturnType(MethodDeclarationSyntax method, string filePath, List<LintError> errors)
        {
            if (method.ReturnType.ToString() != "void")
            {
                AddError(errors, filePath, method.ReturnType,
                    "NetworkCallable methods must return void", LintErrorCodes.NetworkCallable);
            }
        }

        private static void CheckNetworkCallableParameterCount(MethodDeclarationSyntax method, string filePath, List<LintError> errors)
        {
            if (method.ParameterList.Parameters.Count > 8)
            {
                AddError(errors, filePath, method.ParameterList,
                    "NetworkCallable methods cannot have more than 8 parameters", LintErrorCodes.NetworkCallable);
            }
        }

        private static void CheckNetworkCallableRefOutParameters(MethodDeclarationSyntax method, string filePath, List<LintError> errors)
        {
            var refOutParams = method.ParameterList.Parameters
                .Where(p => p.Modifiers.Any(m =>
                    m.IsKind(SyntaxKind.RefKeyword) || m.IsKind(SyntaxKind.OutKeyword)));

            foreach (var param in refOutParams)
            {
                AddError(errors, filePath, param,
                    "NetworkCallable methods cannot have ref/out parameters", LintErrorCodes.NetworkCallable);
            }
        }

        private static void CheckNetworkCallableParamsKeyword(MethodDeclarationSyntax method, string filePath, List<LintError> errors)
        {
            var paramsParams = method.ParameterList.Parameters
                .Where(p => p.Modifiers.Any(m => m.IsKind(SyntaxKind.ParamsKeyword)));

            foreach (var param in paramsParams)
            {
                AddError(errors, filePath, param,
                    "NetworkCallable methods cannot have params parameters", LintErrorCodes.NetworkCallable);
            }
        }

        private static void CheckNetworkCallableDefaultValues(MethodDeclarationSyntax method, string filePath, List<LintError> errors)
        {
            var defaultParams = method.ParameterList.Parameters
                .Where(p => p.Default != null);

            foreach (var param in defaultParams)
            {
                AddError(errors, filePath, param,
                    "NetworkCallable methods cannot have parameters with default values", LintErrorCodes.NetworkCallable);
            }
        }

        private static void CheckNetworkCallableModifiers(MethodDeclarationSyntax method, string filePath, List<LintError> errors)
        {
            var invalidModifiers = new[]
            {
                SyntaxKind.StaticKeyword, SyntaxKind.AbstractKeyword,
                SyntaxKind.VirtualKeyword, SyntaxKind.OverrideKeyword,
                SyntaxKind.SealedKeyword
            };

            if (method.Modifiers.Any(m => invalidModifiers.Contains(m.Kind())))
            {
                AddError(errors, filePath, method,
                    "NetworkCallable methods cannot be static, abstract, virtual, override, or sealed", LintErrorCodes.NetworkCallable);
            }
        }

        /// <summary>
        /// UdonSharp制約: TextMeshProの未公開APIの使用を検出します
        ///
        /// TextMeshProの一部のプロパティやメソッドは、Udon環境で公開されていない場合があります。
        /// このチェックでは、よく使われるが公開されていないTextMeshPro APIを検出し、警告を出します。
        ///
        /// 注意: このチェックは変数名のパターンマッチングに基づいているため、
        /// 誤検出の可能性があります。そのため、エラーではなく警告として報告されます。
        ///
        /// 例:
        /// NG（警告）: tmpText.fontSize = 12; // fontSizeは未公開
        /// OK: tmpText.text = "Hello"; // textは公開済み
        /// </summary>
        private static void CheckTextMeshProAPIs(SyntaxNode root, string filePath, List<LintError> errors)
        {
            // TextMeshPro未公開APIのリスト
            var unexposedTextMeshProAPIs = new HashSet<string>
            {
                "fontSize", "fontSizeMin", "fontSizeMax", "fontStyle", "fontWeight",
                "enableAutoSizing", "fontSharedMaterial", "fontSharedMaterials",
                "fontMaterial", "fontMaterials", "maskable", "isVolumetricText",
                "margin", "textBounds", "preferredWidth", "preferredHeight",
                "flexibleWidth", "flexibleHeight", "minWidth", "minHeight",
                "maxWidth", "maxHeight", "layoutPriority", "isUsingLegacyAnimationComponent",
                "isVolumetricText", "onCullStateChanged", "maskOffset", "renderMode",
                "geometrySortingOrder", "vertexBufferAutoSizeReduction", "firstVisibleCharacter",
                "maxVisibleCharacters", "maxVisibleWords", "maxVisibleLines", "useMaxVisibleDescender",
                "pageToDisplay", "linkedTextComponent", "isTextOverflowing", "firstOverflowCharacterIndex",
                "isTextTruncated", "parseCtrlCharacters", "isOrthographic", "enableCulling",
                "ignoreVisibility", "horizontalMapping", "verticalMapping", "mappingUvLineOffset",
                "enableWordWrapping", "wordWrapingRatios", "overflowMode", "isTextOverflowing",
                "textInfo", "havePropertiesChanged", "isUsingBold", "spriteAnimator",
                "layoutElement", "ignoreRectMaskCulling", "isOverlay"
            };

            // メンバーアクセス式を検出
            var memberAccesses = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>();

            foreach (var memberAccess in memberAccesses)
            {
                // TextMeshProまたはTextMeshProUGUIのインスタンスへのアクセスをチェック
                var memberName = memberAccess.Name.ToString();

                if (unexposedTextMeshProAPIs.Contains(memberName))
                {
                    // 親の型がTextMeshProかどうかをより厳密にチェック
                    var expression = memberAccess.Expression.ToString();

                    // より具体的なパターンマッチング（誤検出を減らす）
                    // TextMeshProUGUI, TextMeshPro, TMP_Text などの型名や明確な変数名のみ
                    if (System.Text.RegularExpressions.Regex.IsMatch(expression,
                        @"\b(TextMeshPro|TextMeshProUGUI|TMP_Text|TMP_InputField|tmpText|tmpPro)\b",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        AddError(errors, filePath, memberAccess,
                            $"Property/Method may not be exposed to Udon: '{expression}.{memberName}' (TextMeshPro)",
                            LintErrorCodes.TextMeshProAPI, DiagnosticSeverity.Warning);
                    }
                }
            }
        }

        /// <summary>
        /// UdonSharp制約: Udonに公開されていない一般的なAPIの使用を検出します
        ///
        /// 以下のような、セキュリティ上の理由やUdonのサンドボックス制約により使用できないAPIを検出します：
        /// - System.Reflection: リフレクションAPI（動的な型操作）
        /// - System.Threading: スレッド関連API（マルチスレッド処理）
        /// - System.IO.File: ファイルI/O API（ファイルシステムアクセス）
        /// - System.Net: ネットワーク通信API（外部通信）
        /// - Application.OpenURL/Quit: アプリケーション制御API
        ///
        /// これらの制約は、VRChatのセキュリティとパフォーマンスを保護するために設けられています。
        ///
        /// 例:
        /// NG: System.Reflection.Assembly.Load()
        /// NG: System.Threading.Thread.Start()
        /// NG: System.IO.File.ReadAllText()
        /// NG: Application.Quit()
        /// </summary>
        private static void CheckGeneralUnexposedAPIs(SyntaxNode root, string filePath, List<LintError> errors)
        {
            // 一般的な未公開メソッド/プロパティのチェック（より厳密に）
            var bannedNamespaces = new Dictionary<string, string>
            {
                { "System.Reflection", "Reflection APIs are not exposed to Udon" },
                { "System.Threading", "Threading APIs are not exposed to Udon" },
                { "System.IO.File", "File I/O APIs are not exposed to Udon" },
                { "System.Net", "Networking APIs are not exposed to Udon" }
            };

            var bannedMethodPatterns = new Dictionary<string, string>
            {
                { @"\bApplication\.OpenURL\b", "Application.OpenURL is not exposed to Udon" },
                { @"\bApplication\.Quit\b", "Application.Quit is not exposed to Udon" }
            };

            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                var invocationString = invocation.ToString();

                // Check for banned namespaces using more precise matching
                foreach (var bannedNamespace in bannedNamespaces)
                {
                    // Use word boundary to avoid false positives like "MySystemReflectionHelper"
                    if (System.Text.RegularExpressions.Regex.IsMatch(invocationString,
                        $@"\b{System.Text.RegularExpressions.Regex.Escape(bannedNamespace.Key)}\b"))
                    {
                        AddError(errors, filePath, invocation, bannedNamespace.Value, LintErrorCodes.UnexposedAPI);
                        break; // Only report once per invocation
                    }
                }

                // Check for banned method patterns
                foreach (var pattern in bannedMethodPatterns)
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(invocationString, pattern.Key))
                    {
                        AddError(errors, filePath, invocation, pattern.Value, LintErrorCodes.UnexposedAPI);
                        break; // Only report once per invocation
                    }
                }
            }
        }

        /// <summary>
        /// UdonSharp制約: 自動プロパティと通常のプロパティは使用できません
        ///
        /// UdonSharpBehaviourでは、C#の自動プロパティ（{ get; set; }）や
        /// 通常のプロパティ（getterとsetterを持つもの）は使用できません。
        /// 例外として、[FieldChangeCallback]属性と組み合わせたプロパティのみ許可されます。
        ///
        /// 例:
        /// NG: public int MyValue { get; set; }
        /// NG: public int MyValue { get { return _value; } set { _value = value; } }
        /// OK: public int myValue; // 通常のフィールド
        /// OK: [FieldChangeCallback(nameof(SyncedValue))] private int _syncedValue;
        ///     public int SyncedValue { get => _syncedValue; set { _syncedValue = value; } }
        /// </summary>
        private static void CheckProperties(SyntaxNode root, string filePath, List<LintError> errors)
        {
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(c => IsUdonSharpBehaviourClass(c));

            foreach (var classDecl in classes)
            {
                var properties = classDecl.Members.OfType<PropertyDeclarationSyntax>();

                foreach (var property in properties)
                {
                    // [FieldChangeCallback]が付いているプロパティは許可
                    var propertyName = property.Identifier.Text;
                    bool isFieldChangeCallbackPattern = classDecl.Members
                        .OfType<FieldDeclarationSyntax>()
                        .Any(f => f.AttributeLists.Any(al =>
                            al.Attributes.Any(a =>
                                a.Name.ToString().Contains("FieldChangeCallback") &&
                                a.ArgumentList?.Arguments.Any(arg =>
                                    arg.ToString().Contains(propertyName)) == true)));

                    if (!isFieldChangeCallbackPattern)
                    {
                        AddError(errors, filePath, property,
                            "Properties are not supported in UdonSharp (except when used with [FieldChangeCallback])", LintErrorCodes.Property);
                    }
                }
            }
        }

        /// <summary>
        /// UdonSharp制約: メソッドオーバーロードは使用できません
        ///
        /// UdonSharpBehaviourでは、同じ名前で異なる引数を持つメソッド（オーバーロード）を
        /// 定義することができません。メソッド名は一意である必要があります。
        ///
        /// 例:
        /// NG: public void Process(int value) { }
        ///     public void Process(string value) { }
        /// OK: public void ProcessInt(int value) { }
        ///     public void ProcessString(string value) { }
        /// </summary>
        private static void CheckMethodOverloads(SyntaxNode root, string filePath, List<LintError> errors)
        {
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(c => IsUdonSharpBehaviourClass(c));

            foreach (var classDecl in classes)
            {
                var methods = classDecl.Members.OfType<MethodDeclarationSyntax>()
                    .GroupBy(m => m.Identifier.Text)
                    .Where(g => g.Count() > 1);

                foreach (var methodGroup in methods)
                {
                    // 最初のメソッド以外をエラーとして報告
                    foreach (var method in methodGroup.Skip(1))
                    {
                        AddError(errors, filePath, method,
                            $"Method overloads are not supported in UdonSharp: '{method.Identifier.Text}'", LintErrorCodes.MethodOverload);
                    }
                }
            }
        }

        /// <summary>
        /// UdonSharp制約: インターフェースの実装は使用できません
        ///
        /// UdonSharpBehaviourでは、interfaceを実装することができません。
        /// 抽象化が必要な場合は、継承やパターンベースの設計を検討してください。
        ///
        /// 例:
        /// NG: public class MyBehaviour : UdonSharpBehaviour, IMyInterface { }
        /// OK: public class MyBehaviour : UdonSharpBehaviour { }
        /// </summary>
        private static void CheckInterfaces(SyntaxNode root, string filePath, List<LintError> errors)
        {
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(c => IsUdonSharpBehaviourClass(c));

            foreach (var classDecl in classes)
            {
                if (classDecl.BaseList != null)
                {
                    var interfaces = classDecl.BaseList.Types
                        .Where(t => !t.Type.ToString().Contains("UdonSharpBehaviour"));

                    foreach (var interfaceType in interfaces)
                    {
                        AddError(errors, filePath, interfaceType,
                            "Interface implementation is not supported in UdonSharp", LintErrorCodes.Interface);
                    }
                }
            }
        }

        /// <summary>
        /// UdonSharp制約: ジェネリッククラスは使用できません
        ///
        /// UdonSharpBehaviourでは、型パラメータを持つジェネリッククラスを定義できません。
        /// ジェネリックメソッドも同様に使用できません。
        ///
        /// 例:
        /// NG: public class MyBehaviour&lt;T&gt; : UdonSharpBehaviour { }
        /// OK: public class MyBehaviour : UdonSharpBehaviour { }
        /// </summary>
        private static void CheckGenericClasses(SyntaxNode root, string filePath, List<LintError> errors)
        {
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(c => IsUdonSharpBehaviourClass(c) && c.TypeParameterList != null);

            foreach (var classDecl in classes)
            {
                if (classDecl.TypeParameterList != null)
                {
                    AddError(errors, filePath, classDecl.TypeParameterList,
                        "Generic classes are not supported in UdonSharp", LintErrorCodes.GenericClass);
                }
            }
        }

        /// <summary>
        /// UdonSharp制約: 別ファイルで定義されたカスタムクラスのフィールドアクセスは非サポート
        ///
        /// UdonSharpコンパイラは、別ファイルで定義された[System.Serializable]クラスの
        /// フィールドへの直接アクセスをサポートしていません。
        /// また、複数のファイルで使用されているカスタムクラスのフィールドアクセスもサポートされていません。
        /// これらのクラス定義は、使用するUdonSharpBehaviourと同じファイル内に配置する必要があります。
        ///
        /// セマンティック解析を使用して、実際に別ファイルで定義されたカスタムクラスの
        /// フィールドアクセス、または複数のファイルで使用されているカスタムクラスのフィールドアクセスを検出します。
        ///
        /// 例:
        /// NG: ColorPaletteItem.csでColorPaletteItemを定義し、MoguManager.csでcp.mainColorにアクセス
        /// NG: MoguManager.csでColorPaletteItemを定義し、ColorPaletteData.csでも使用し、MoguManager.csでcp.mainColorにアクセス
        /// OK: MoguManager.cs内でColorPaletteItemを定義し、MoguManager.csでのみ使用
        /// </summary>
        private static void CheckCrossFileFieldAccess(SyntaxNode root, string filePath, List<LintError> errors, CSharpCompilation compilation)
        {
            var tree = root.SyntaxTree;
            var semanticModel = compilation.GetSemanticModel(tree);

            if (semanticModel == null)
                return;

            var memberAccesses = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>();

            foreach (var memberAccess in memberAccesses)
            {
                try
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);

                    if (symbolInfo.Symbol is IFieldSymbol fieldSymbol)
                    {
                        var containingType = fieldSymbol.ContainingType;

                        // カスタムシリアライズ可能クラスか判定
                        if (IsCustomSerializableClass(containingType))
                        {
                            // 型の定義場所を取得
                            var typeLocation = containingType.Locations.FirstOrDefault();
                            var accessLocation = memberAccess.GetLocation();

                            if (typeLocation != null && accessLocation != null)
                            {
                                var typeFilePath = typeLocation.SourceTree?.FilePath;
                                var accessFilePath = accessLocation.SourceTree?.FilePath;

                                if (!string.IsNullOrEmpty(typeFilePath) && !string.IsNullOrEmpty(accessFilePath))
                                {
                                    var typeFilePathNormalized = Path.GetFullPath(typeFilePath);
                                    var accessFilePathNormalized = Path.GetFullPath(accessFilePath);

                                    // 型が別ファイルで定義されている場合
                                    if (typeFilePathNormalized != accessFilePathNormalized)
                                    {
                                        AddError(
                                            errors,
                                            filePath,
                                            memberAccess,
                                            $"UdonSharp does not support field access to custom classes defined in other files. " +
                                            $"Type '{containingType.Name}' is defined in '{Path.GetFileName(typeFilePath)}'. " +
                                            $"Move the class definition to this file as a top-level class.",
                                            LintErrorCodes.CrossFileFieldAccess
                                        );
                                    }
                                    // 型が同じファイルで定義されている場合でも、他のファイルで使用されているかチェック
                                    else
                                    {
                                        if (IsTypeUsedInMultipleFiles(containingType, compilation, typeFilePathNormalized))
                                        {
                                            AddError(
                                                errors,
                                                filePath,
                                                memberAccess,
                                                $"UdonSharp does not support field access to custom classes that are shared across multiple files. " +
                                                $"Type '{containingType.Name}' is defined in this file but also used in other files. " +
                                                $"Custom serializable classes must be defined and used only within a single file.",
                                                LintErrorCodes.CrossFileFieldAccess
                                            );
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // セマンティック解析が失敗した場合は無視（型情報が不完全な可能性）
                }
            }
        }

        /// <summary>
        /// 別ファイルで定義されたカスタムクラスのメソッド呼び出しをチェック
        /// UdonSharpは別ファイルで定義されたカスタムシリアライズ可能クラスのインスタンスメソッド呼び出しをサポートしない
        /// </summary>
        /// <remarks>
        /// エラー例:
        /// - ColorPaletteData.csでColorPaletteItemを定義し、Mogu.csでpalette.GetMainColor()を呼び出す
        ///
        /// このチェックは、カスタムシリアライズ可能クラス([System.Serializable])の
        /// インスタンスメソッド呼び出しが、クラス定義とは別のファイルで行われている場合にエラーを報告します。
        /// </remarks>
        private static void CheckCrossFileMethodInvocation(SyntaxNode root, string filePath, List<LintError> errors, CSharpCompilation compilation)
        {
            var tree = root.SyntaxTree;
            var semanticModel = compilation.GetSemanticModel(tree);

            if (semanticModel == null)
                return;

            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                try
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(invocation);

                    if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
                    {
                        // 静的メソッドは除外（静的メソッドは別のチェックで処理）
                        if (methodSymbol.IsStatic)
                            continue;

                        var containingType = methodSymbol.ContainingType;

                        // カスタムシリアライズ可能クラスか判定
                        if (IsCustomSerializableClass(containingType))
                        {
                            // 型の定義場所を取得
                            var typeLocation = containingType.Locations.FirstOrDefault();
                            var accessLocation = invocation.GetLocation();

                            if (typeLocation != null && accessLocation != null)
                            {
                                var typeFilePath = typeLocation.SourceTree?.FilePath;
                                var accessFilePath = accessLocation.SourceTree?.FilePath;

                                if (!string.IsNullOrEmpty(typeFilePath) && !string.IsNullOrEmpty(accessFilePath))
                                {
                                    var typeFilePathNormalized = Path.GetFullPath(typeFilePath);
                                    var accessFilePathNormalized = Path.GetFullPath(accessFilePath);

                                    // 型が別ファイルで定義されている場合
                                    if (typeFilePathNormalized != accessFilePathNormalized)
                                    {
                                        AddError(
                                            errors,
                                            filePath,
                                            invocation,
                                            $"UdonSharp does not support method invocations on custom classes defined in other files. " +
                                            $"Method '{methodSymbol.Name}' on type '{containingType.Name}' is defined in '{Path.GetFileName(typeFilePath)}'. " +
                                            $"Use field access or refactor to return values directly from the owning class.",
                                            LintErrorCodes.CrossFileMethodInvocation
                                        );
                                    }
                                    // 型が同じファイルで定義されている場合でも、他のファイルで使用されているかチェック
                                    else
                                    {
                                        if (IsTypeUsedInMultipleFiles(containingType, compilation, typeFilePathNormalized))
                                        {
                                            AddError(
                                                errors,
                                                filePath,
                                                invocation,
                                                $"UdonSharp does not support method invocations on custom classes that are shared across multiple files. " +
                                                $"Method '{methodSymbol.Name}' on type '{containingType.Name}' is defined in this file but the type is also used in other files. " +
                                                $"Custom serializable classes must be defined and used only within a single file.",
                                                LintErrorCodes.CrossFileMethodInvocation
                                            );
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // セマンティック解析が失敗した場合は無視（型情報が不完全な可能性）
                }
            }
        }

        /// <summary>
        /// UdonSharpBehaviour内での[System.Serializable]クラス使用をチェック（UDON025）
        /// </summary>
        /// <remarks>
        /// UdonSharpは[System.Serializable]クラスを完全にサポートしていません:
        /// - フィールド直接アクセス → ImportedUdonSharpFieldSymbol エラー
        /// - メソッド呼び出し → BoundInvocationExpression エラー
        ///
        /// エラー例:
        /// [System.Serializable]
        /// public class ColorPaletteItem { public Color mainColor; }
        ///
        /// UdonSharpBehaviour内で:
        /// - palette.mainColor （NG: フィールドアクセス）
        /// - palette.GetColor() （NG: メソッド呼び出し）
        ///
        /// 解決方法:
        /// public class ColorPaletteItem : UdonSharpBehaviour { public Color mainColor; }
        /// </remarks>
        private static void CheckUdonBehaviourSerializableClassUsage(SyntaxNode root, string filePath, List<LintError> errors, CSharpCompilation compilation)
        {
            var tree = root.SyntaxTree;
            var semanticModel = compilation.GetSemanticModel(tree);

            if (semanticModel == null)
                return;

            // このファイルにUdonSharpBehaviourを継承したクラスがあるか確認
            var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            bool hasUdonSharpBehaviourClass = false;

            foreach (var classDecl in classDeclarations)
            {
                if (IsUdonSharpBehaviourClass(classDecl))
                {
                    hasUdonSharpBehaviourClass = true;
                    break;
                }
            }

            // UdonSharpBehaviourクラスがない場合はチェックスキップ
            if (!hasUdonSharpBehaviourClass)
                return;

            // 全メンバーアクセスを走査（フィールドアクセス）
            var memberAccesses = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>();

            foreach (var memberAccess in memberAccesses)
            {
                try
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);

                    if (symbolInfo.Symbol is IFieldSymbol fieldSymbol)
                    {
                        var containingType = fieldSymbol.ContainingType;

                        // [System.Serializable]で非UdonSharpBehaviourクラスか判定
                        if (IsCustomSerializableClass(containingType))
                        {
                            if (!InheritsFromUdonSharpBehaviour(containingType))
                            {
                                string fieldName = fieldSymbol.Name;
                                string typeName = containingType.Name;

                                AddError(
                                    errors,
                                    filePath,
                                    memberAccess,
                                    $"UdonSharp does not support [System.Serializable] classes. " +
                                    $"Type '{typeName}' must inherit from UdonSharpBehaviour. " +
                                    $"Consider converting '{typeName}' to a UdonSharpBehaviour class.",
                                    LintErrorCodes.UdonBehaviourSerializableClassUsage
                                );
                            }
                        }
                    }
                }
                catch
                {
                    // セマンティック解析が失敗した場合は無視（型情報が不完全な可能性）
                }
            }
        }

        /// <summary>
        /// 型が複数のファイルで使用されているか判定
        /// </summary>
        private static bool IsTypeUsedInMultipleFiles(INamedTypeSymbol typeSymbol, CSharpCompilation compilation, string definitionFilePath)
        {
            // コンパイル内のすべての構文木を検索
            foreach (var tree in compilation.SyntaxTrees)
            {
                var currentFilePath = Path.GetFullPath(tree.FilePath);

                // 定義ファイルはスキップ
                if (currentFilePath == definitionFilePath)
                    continue;

                try
                {
                    var semanticModel = compilation.GetSemanticModel(tree);
                    var root = tree.GetRoot();

                    // この構文木で型が参照されているかチェック
                    var identifiers = root.DescendantNodes().OfType<IdentifierNameSyntax>();

                    foreach (var identifier in identifiers)
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(identifier);

                        // 型として参照されているかチェック
                        if (symbolInfo.Symbol is INamedTypeSymbol referencedType)
                        {
                            if (SymbolEqualityComparer.Default.Equals(referencedType, typeSymbol))
                            {
                                return true; // 別のファイルで使用されている
                            }
                        }
                    }
                }
                catch
                {
                    // セマンティック解析が失敗した場合は無視
                }
            }

            return false;
        }

        /// <summary>
        /// カスタムシリアライズ可能クラスか判定
        /// Unity組み込み型やUdonSharpBehaviourを除外し、[System.Serializable]属性を持つクラスを検出
        /// </summary>
        private static bool IsCustomSerializableClass(INamedTypeSymbol typeSymbol)
        {
            if (typeSymbol == null)
                return false;

            // Unity組み込み型を除外
            var unityNamespaces = new[] { "UnityEngine", "VRC.SDKBase", "VRC.Udon", "TMPro", "UdonSharp" };
            var namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "";

            if (unityNamespaces.Any(ns => namespaceName.StartsWith(ns)))
            {
                return false;
            }

            // UdonSharpBehaviourを継承しているクラスは除外
            if (InheritsFromUdonSharpBehaviour(typeSymbol))
            {
                return false;
            }

            // [System.Serializable]属性を持つクラスか確認
            var hasSerializableAttribute = typeSymbol.GetAttributes().Any(attr =>
                attr.AttributeClass?.Name == "SerializableAttribute" ||
                attr.AttributeClass?.ToDisplayString() == "System.SerializableAttribute");

            return hasSerializableAttribute;
        }

        /// <summary>
        /// UdonSharpBehaviourを継承しているか判定
        /// </summary>
        private static bool InheritsFromUdonSharpBehaviour(INamedTypeSymbol typeSymbol)
        {
            var current = typeSymbol.BaseType;
            while (current != null)
            {
                if (current.Name == "UdonSharpBehaviour")
                    return true;
                current = current.BaseType;
            }
            return false;
        }

        /// <summary>
        /// UdonSharpスクリプトから呼び出される静的メソッドのコールグラフを構築
        /// </summary>
        private static Dictionary<string, HashSet<string>> BuildCallGraph(CSharpCompilation compilation, List<string> udonSharpFiles)
        {
            // Key: 静的メソッドのファイルパス, Value: そのメソッドを呼び出しているUdonSharpファイルのセット
            var callGraph = new Dictionary<string, HashSet<string>>();

            foreach (var udonSharpFile in udonSharpFiles)
            {
                var tree = compilation.SyntaxTrees.FirstOrDefault(t => Path.GetFullPath(t.FilePath) == Path.GetFullPath(udonSharpFile));
                if (tree == null) continue;

                var semanticModel = compilation.GetSemanticModel(tree);
                if (semanticModel == null) continue;

                var root = tree.GetRoot();
                var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

                foreach (var invocation in invocations)
                {
                    try
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                        if (symbolInfo.Symbol is IMethodSymbol methodSymbol && methodSymbol.IsStatic)
                        {
                            // 静的メソッドの定義位置を取得
                            var methodLocation = methodSymbol.Locations.FirstOrDefault();
                            if (methodLocation != null && methodLocation.SourceTree != null)
                            {
                                var staticMethodFile = Path.GetFullPath(methodLocation.SourceTree.FilePath);

                                // UdonSharpファイル内の静的メソッドはスキップ（既にチェック済み）
                                if (staticMethodFile == Path.GetFullPath(udonSharpFile))
                                    continue;

                                // コールグラフに追加
                                if (!callGraph.ContainsKey(staticMethodFile))
                                {
                                    callGraph[staticMethodFile] = new HashSet<string>();
                                }
                                callGraph[staticMethodFile].Add(udonSharpFile);
                            }
                        }
                    }
                    catch
                    {
                        // セマンティック解析失敗時は無視
                    }
                }
            }

            return callGraph;
        }

        /// <summary>
        /// UdonSharp制約: UdonSharpから呼び出される静的メソッド内でのカスタムクラスフィールドアクセスは非サポート
        ///
        /// UdonSharpスクリプトから呼び出される静的メソッド内で、[System.Serializable]クラスの
        /// フィールドに直接アクセスすることはサポートされていません。
        /// これは、UdonSharpコンパイラが静的メソッドを解析する際に、ImportedUdonSharpFieldSymbolとして
        /// 扱い、フィールドアクセスが実装されていないためです。
        ///
        /// 例:
        /// NG: public static class ColorPaletteData {
        ///         public static ColorPaletteItem FindPalette() {
        ///             return item.mainColor; // フィールドアクセス
        ///         }
        ///     }
        /// OK: UdonSharpBehaviour内でフィールドアクセスを行う
        /// </summary>
        private static void CheckStaticMethodFieldAccess(SyntaxNode root, string filePath, List<LintError> errors, CSharpCompilation compilation, HashSet<string> callingUdonSharpFiles)
        {
            if (callingUdonSharpFiles == null || callingUdonSharpFiles.Count == 0)
                return;

            var tree = root.SyntaxTree;
            var semanticModel = compilation.GetSemanticModel(tree);
            if (semanticModel == null)
                return;

            // UdonSharpから呼び出される静的メソッドを含むファイルはチェック対象
            // (純粋なC#クラスでも、UdonSharpから呼ばれればUdonSharp制約が適用される)
            // 注: callingUdonSharpFiles の null/空チェックは1355-1356で既に実施済み

            // このファイル内のすべての静的メソッドを取得
            var staticMethods = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.StaticKeyword)));

            foreach (var method in staticMethods)
            {
                // メソッド内のすべてのメンバーアクセスをチェック
                var memberAccesses = method.DescendantNodes().OfType<MemberAccessExpressionSyntax>();

                foreach (var memberAccess in memberAccesses)
                {
                    try
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);

                        if (symbolInfo.Symbol is IFieldSymbol fieldSymbol)
                        {
                            var containingType = fieldSymbol.ContainingType;

                            // カスタムシリアライズ可能クラスか判定
                            if (IsCustomSerializableClass(containingType))
                            {
                                AddError(
                                    errors,
                                    filePath,
                                    memberAccess,
                                    $"UdonSharp does not support field access to custom classes in static methods called from UdonSharp. " +
                                    $"Field '{fieldSymbol.Name}' of type '{containingType.Name}' is accessed in static method '{method.Identifier.Text}'. " +
                                    $"Move the logic to a UdonSharpBehaviour class where field access is supported.",
                                    LintErrorCodes.StaticMethodFieldAccess
                                );
                            }
                        }
                    }
                    catch
                    {
                        // セマンティック解析が失敗した場合は無視
                    }
                }
            }
        }

        #endregion

        private static bool IsUdonSharpBehaviourClass(ClassDeclarationSyntax classDecl)
        {
            // Check if the class inherits from UdonSharpBehaviour
            if (classDecl.BaseList != null)
            {
                return classDecl.BaseList.Types
                    .Any(t => t.Type.ToString().Contains("UdonSharpBehaviour"));
            }
            return false;
        }
    }
}