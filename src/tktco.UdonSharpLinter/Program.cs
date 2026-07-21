using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace tktco.UdonSharpLinter
{
    internal class Program
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
            // README生成モード
            if (args.Length > 0 && args[0] == "--generate-readme")
            {
                READMEGenerator.Generate();
                return 0;
            }

            if (args.Length == 0)
            {
                Console.WriteLine("Usage: udonsharp-lint <directory_path> [--exclude-test-scripts]");
                Console.WriteLine("       udonsharp-lint --generate-readme");
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
                .Where(f => !f.Contains("\\Temp\\") && !f.Contains("\\Library\\") && !f.Contains("\\obj\\") && !f.Contains("\\bin\\")
                         && !f.Contains("/Temp/") && !f.Contains("/Library/") && !f.Contains("/obj/") && !f.Contains("/bin/"))
                .Where(f => !f.Contains("\\Editor\\") && !f.Contains("\\editor\\")
                         && !f.Contains("/Editor/") && !f.Contains("/editor/")) // Exclude Editor scripts
                .Where(f => !excludeTestScripts || (!f.Contains("\\TestScripts\\") && !f.Contains("\\Tests\\") && !f.Contains("\\Test\\")
                                                 && !f.Contains("/TestScripts/") && !f.Contains("/Tests/") && !f.Contains("/Test/"))) // Optionally exclude test scripts
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

            // UdonSharpスクリプトから参照されるユーザー定義型のグラフを構築
            var typeReferenceGraph = BuildTypeReferenceGraph(compilation, filteredFiles);

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

            // UdonSharpから参照されるユーザー定義型を含むファイルもチェック
            foreach (var entry in typeReferenceGraph)
            {
                var referencedFile = entry.Key;
                var referencedTypes = entry.Value;

                if (syntaxTreeDict.TryGetValue(referencedFile, out var tree))
                {
                    LintReferencedTypeFile(referencedFile, tree, compilation, referencedTypes);
                }
            }

            Console.WriteLine($"\n[UdonSharp Linter] Summary: {_errorCount} errors, {_warningCount} warnings");

            return _hasErrors ? 1 : 0;
        }

        private static readonly Lazy<List<MetadataReference>> _trustedPlatformAssemblyReferences =
            new Lazy<List<MetadataReference>>(LoadTrustedPlatformAssemblyReferences);

        /// <summary>
        /// .NETランタイムの信頼済みプラットフォームアセンブリ(BCL全体)を参照リストとして取得する。
        /// typeof(X).Assemblyを個別に参照するだけでは、型転送されたBCL型(例: IEnumerable&lt;T&gt;)が
        /// 解決できない場合があるため、実行中のランタイムが持つアセンブリ一覧を丸ごと利用する。
        /// プロセス内で一度だけ読み込みキャッシュし、呼び出し元が結果に追加できるよう毎回コピーを返す
        /// </summary>
        private static List<MetadataReference> GetTrustedPlatformAssemblyReferences()
        {
            return new List<MetadataReference>(_trustedPlatformAssemblyReferences.Value);
        }

        private static List<MetadataReference> LoadTrustedPlatformAssemblyReferences()
        {
            var assemblyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var trustedAssembliesPaths = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (!string.IsNullOrEmpty(trustedAssembliesPaths))
            {
                foreach (var path in trustedAssembliesPaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    assemblyPaths.Add(path);
                }
            }

            // TRUSTED_PLATFORM_ASSEMBLIESが取得できない特殊なホスト環境向けのフォールバック:
            // 現在のAssemblyLoadContextに読み込み済みのアセンブリも参照に加える
            foreach (var assembly in System.Runtime.Loader.AssemblyLoadContext.Default.Assemblies)
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    assemblyPaths.Add(assembly.Location);
                }
            }

            if (assemblyPaths.Count == 0)
            {
                assemblyPaths.Add(typeof(object).Assembly.Location);
                assemblyPaths.Add(typeof(Console).Assembly.Location);
                assemblyPaths.Add(typeof(System.Linq.Enumerable).Assembly.Location);
            }

            return assemblyPaths
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
                .ToList();
        }

        /// <summary>
        /// コンパイル情報を構築
        /// セマンティック解析に必要な型情報を提供
        /// </summary>
        private static CSharpCompilation CreateCompilation(List<SyntaxTree> syntaxTrees, string? scriptAssembliesDirOverride = null)
        {
            // 基本的な参照アセンブリを追加
            // .NET Core以降はBCLの型が複数のアセンブリに型転送されているため(例: IEnumerable<T>はSystem.Runtime)、
            // typeof(object).Assembly等を個別に参照するだけでは不十分。ランタイムの信頼済みアセンブリを
            // 一括で参照に加えることで、System.Linq等のBCL型をセマンティック解析(CheckLinqUsage)で正しく解決できるようにする
            var references = GetTrustedPlatformAssemblyReferences();

            // Unity/UdonSharp参照アセンブリを追加（存在する場合）
            // 注: Library/ScriptAssembliesにはUnity/VRC SDKだけでなく、プロジェクト自身の
            // コンパイル済みスクリプトアセンブリ(Assembly-CSharp.dll等)も含まれる。同名の型が
            // ソース(構文木)側とこの参照側の両方に存在する場合でも、C#コンパイラはCS0436警告を
            // 出しつつソース側の型定義を優先して解決するため、クロスファイルチェックが依存する
            // シンボルのSourceTree解決は壊れない(スクリプトアセンブリだけを除外する必要はない)
            var scriptAssembliesDir = scriptAssembliesDirOverride
                ?? Path.Combine(Directory.GetCurrentDirectory(), "Library", "ScriptAssemblies");
            references.AddRange(LoadUnityAssemblyReferences(scriptAssembliesDir));

            return CSharpCompilation.Create(
                "UdonSharpLinter",
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
        }

        /// <summary>
        /// Library/ScriptAssemblies配下の*.dllを全て走査し、参照アセンブリとして読み込む。
        /// 近年のUnity(2018.1+)はUnityEngine/VRC SDKをモジュールごとに多数のDLL
        /// (UnityEngine.CoreModule.dll、VRC.Udon.dll等)へ分割しているため、
        /// 特定のファイル名だけを決め打ちで探すと実際のプロジェクトではほぼヒットしない。
        /// 個別のDLLが不正/読み込み不可でも他のDLLの読み込みを妨げないよう、ファイル単位でエラーを捕捉する
        /// </summary>
        internal static List<MetadataReference> LoadUnityAssemblyReferences(string scriptAssembliesDir)
        {
            var references = new List<MetadataReference>();

            if (!Directory.Exists(scriptAssembliesDir))
            {
                return references;
            }

            foreach (var dllPath in Directory.GetFiles(scriptAssembliesDir, "*.dll"))
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(dllPath));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Warning] Could not load assembly {dllPath}: {ex.Message}");
                }
            }

            return references;
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
                // Note: Properties are now supported in UdonSharp 1.0+
                // CheckProperties(root, filePath, errors);
                CheckMethodOverloads(root, filePath, errors);
                CheckInterfaces(root, filePath, errors);
                CheckCrossFileFieldAccess(root, filePath, errors, compilation);
                CheckCrossFileMethodInvocation(root, filePath, errors, compilation);
                CheckUdonBehaviourSerializableClassUsage(root, filePath, errors, compilation);
                CheckSendCustomEventMethods(root, filePath, errors, compilation);
                CheckNullConditionalOperators(root, filePath, errors);
                // Note: Null coalescing operator (??) is now supported in UdonSharp
                // CheckNullCoalescingOperators(root, filePath, errors);
                CheckAsyncAwait(root, filePath, errors);
                CheckGotoStatements(root, filePath, errors);
                CheckUserDefinedTypeStaticFieldAccess(root, filePath, errors, compilation);
                CheckGenericCollectionTypes(root, filePath, errors);
                CheckLinqUsage(root, filePath, errors, compilation);
                CheckLambdaAndDelegates(root, filePath, errors);
                CheckCoroutineUsage(root, filePath, errors);
                CheckUIEventListenerRegistration(root, filePath, errors);
                CheckGenericGetComponentUdonBehaviour(root, filePath, errors);
                CheckSynchronizationConstraints(root, filePath, errors);

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

        /// <summary>
        /// UdonSharpから参照されるユーザー定義型を含むファイルのlint
        /// </summary>
        private static void LintReferencedTypeFile(string filePath, SyntaxTree tree, CSharpCompilation compilation, HashSet<string> referencedTypes)
        {
            try
            {
                var root = tree.GetRoot();
                var errors = new List<LintError>();

                // 参照されたユーザー定義型の静的フィールド定義をチェック
                CheckReferencedTypeStaticFields(root, filePath, errors, referencedTypes);

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

        internal class LintError
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
        /// - 15: Removed - Properties are now supported in UdonSharp 1.0+
        /// - 28: Removed - Null coalescing operator (??) is now supported in UdonSharp
        ///
        /// Error code ranges (see README.md/README.ja.md "Checks" tables, regenerated via --generate-readme,
        /// for the authoritative code-to-category mapping):
        /// - Basic language feature restrictions: 1-9, 11, 12, 18, 27, 29, 30, 32-35
        /// - API and attribute restrictions: 13, 14, 16, 17, 19, 26, 36, 37
        /// - Cross-file and semantic analysis: 20-22, 25, 31
        /// - Networking and synchronization: 38-41
        /// </summary>
        internal static class LintErrorCodes
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
            // Note: Property (15) is no longer used - Properties are now supported in UdonSharp 1.0+
            [Obsolete("Properties are now supported in UdonSharp 1.0+")]
            public const int Property = 15;
            public const int MethodOverload = 16;
            public const int Interface = 17;

            // Cross-file and semantic analysis
            public const int CrossFileFieldAccess = 20;
            public const int StaticMethodFieldAccess = 21;
            public const int CrossFileMethodInvocation = 22;
            public const int UdonBehaviourSerializableClassUsage = 25;

            // Additional language feature restrictions
            public const int SendCustomEventMethodNotFound = 26;
            public const int NullConditionalOperator = 27;
            // Note: NullCoalescingOperator (28) is no longer used - ?? is now supported in UdonSharp
            [Obsolete("Null coalescing operator (??) is now supported in UdonSharp")]
            public const int NullCoalescingOperator = 28;
            public const int AsyncAwait = 29;
            public const int GotoStatement = 30;
            public const int UserDefinedTypeStaticFieldAccess = 31;

            // Additional language feature restrictions (from agent-skills-vrc-udon review)
            public const int GenericCollectionType = 32;
            public const int LinqUsage = 33;
            public const int LambdaOrDelegate = 34;
            public const int CoroutineUsage = 35;

            // API and attribute restrictions
            public const int UIEventListenerRegistration = 36;
            public const int GenericGetComponentUdonBehaviour = 37;

            // Networking and synchronization checks
            public const int SyncModeConflict = 38;
            public const int ManualSyncMissingRequestSerialization = 39;
            public const int ExcessiveSyncedVariables = 40;
            public const int LargeArraySynced = 41;
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
                al.Attributes.Any(a => IsAttributeNameMatch(a.Name, attributeName)));
        }

        /// <summary>
        /// Helper method to get the [UdonBehaviourSyncMode(...)] argument text of a class, or null if absent
        /// </summary>
        private static string GetUdonBehaviourSyncMode(ClassDeclarationSyntax classDecl)
        {
            return classDecl.AttributeLists
                .SelectMany(al => al.Attributes)
                .FirstOrDefault(a => IsAttributeNameMatch(a.Name, "UdonBehaviourSyncMode"))
                ?.ArgumentList?.Arguments.FirstOrDefault()?.ToString();
        }

        /// <summary>
        /// Compares an attribute usage's name against a target simple name exactly, rather than
        /// via substring matching, so a decoy attribute like [UdonSyncedMetadata] doesn't get
        /// mistaken for [UdonSynced]. Works on the name's syntax node instead of its string form,
        /// which also handles alias-qualified names like [global::NetworkCallable] that a
        /// last-dot string split misses. C# allows omitting the "Attribute" suffix at the usage
        /// site, so both forms are accepted.
        /// </summary>
        private static bool IsAttributeNameMatch(NameSyntax actualAttributeName, string attributeName)
        {
            var simpleName = actualAttributeName switch
            {
                QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
                AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
                SimpleNameSyntax simple => simple.Identifier.ValueText,
                _ => actualAttributeName.ToString(),
            };
            return simpleName == attributeName || simpleName == attributeName + "Attribute";
        }

        /// <summary>
        /// Helper method to check whether an invocation calls a method with the given bare name,
        /// whether called unqualified (IdentifierNameSyntax) or via member access (MemberAccessExpressionSyntax)
        /// </summary>
        private static bool IsInvocationOf(InvocationExpressionSyntax invocation, string methodName)
        {
            return (invocation.Expression is IdentifierNameSyntax identifier && identifier.Identifier.Text == methodName) ||
                   (invocation.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.Text == methodName);
        }

        /// <summary>
        /// レシーバー式がUnityEventのフィールド命名規則(onClick, onValueChangedなど)に一致するか判定する。
        /// `button.onClick`のようなメンバーアクセスと、`onReady`のように直接公開されたフィールド/プロパティの両方を許容する
        /// </summary>
        private static bool IsUnityEventLikeReceiver(ExpressionSyntax receiver)
        {
            return (receiver is IdentifierNameSyntax identifier && identifier.Identifier.Text.StartsWith("on", StringComparison.OrdinalIgnoreCase)) ||
                   (receiver is MemberAccessExpressionSyntax member && member.Name.Identifier.Text.StartsWith("on", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 呼び出しのレシーバーが暗黙のthis、明示的な`this.`、または`base.`であるか判定する
        /// (他のオブジェクトに対する呼び出し、例: other.RequestSerialization()を除外するため)
        /// </summary>
        private static bool IsSelfReceiverInvocation(InvocationExpressionSyntax invocation)
        {
            return invocation.Expression is IdentifierNameSyntax ||
                   (invocation.Expression is MemberAccessExpressionSyntax member &&
                    (member.Expression is ThisExpressionSyntax || member.Expression is BaseExpressionSyntax));
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
        /// [DEPRECATED - UdonSharp 1.0+でサポート] プロパティは使用可能になりました
        ///
        /// UdonSharp 1.0以降、C#のプロパティ（自動プロパティおよび通常のプロパティ）がサポートされています。
        /// [FieldChangeCallback]属性と組み合わせることで、ネットワーク同期時のセッター呼び出しも可能です。
        ///
        /// 例:
        /// OK: public int MyValue { get; set; }
        /// OK: public int MyValue { get { return _value; } set { _value = value; } }
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
        /// UdonSharpスクリプトから参照されるユーザー定義型のグラフを構築
        /// Key: ユーザー定義型が定義されているファイルパス
        /// Value: そのファイルで定義されている、UdonSharpから参照される型名のセット
        /// </summary>
        private static Dictionary<string, HashSet<string>> BuildTypeReferenceGraph(CSharpCompilation compilation, List<string> udonSharpFiles)
        {
            var typeReferenceGraph = new Dictionary<string, HashSet<string>>();

            foreach (var udonSharpFile in udonSharpFiles)
            {
                var tree = compilation.SyntaxTrees.FirstOrDefault(t => Path.GetFullPath(t.FilePath) == Path.GetFullPath(udonSharpFile));
                if (tree == null) continue;

                var semanticModel = compilation.GetSemanticModel(tree);
                if (semanticModel == null) continue;

                var root = tree.GetRoot();

                // UdonSharpBehaviourクラス内のコードのみをチェック
                var udonSharpClasses = FindUdonSharpBehaviourClasses(root);

                foreach (var classDecl in udonSharpClasses)
                {
                    // メンバーアクセス（SomeClass.StaticField）を検出
                    var memberAccesses = classDecl.DescendantNodes().OfType<MemberAccessExpressionSyntax>();

                    foreach (var memberAccess in memberAccesses)
                    {
                        try
                        {
                            var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);

                            // フィールドアクセスを検出
                            if (symbolInfo.Symbol is IFieldSymbol fieldSymbol && fieldSymbol.IsStatic)
                            {
                                var containingType = fieldSymbol.ContainingType;
                                if (containingType != null && IsUserDefinedType(containingType))
                                {
                                    var typeLocation = containingType.Locations.FirstOrDefault();
                                    if (typeLocation != null && typeLocation.SourceTree != null)
                                    {
                                        var typeFile = Path.GetFullPath(typeLocation.SourceTree.FilePath);

                                        // 同じファイルはスキップ
                                        if (typeFile == Path.GetFullPath(udonSharpFile))
                                            continue;

                                        if (!typeReferenceGraph.ContainsKey(typeFile))
                                        {
                                            typeReferenceGraph[typeFile] = new HashSet<string>();
                                        }
                                        typeReferenceGraph[typeFile].Add(containingType.Name);
                                    }
                                }
                            }

                            // メソッド呼び出し（静的メソッド）を検出して、その型も追跡
                            if (symbolInfo.Symbol is IMethodSymbol methodSymbol && methodSymbol.IsStatic)
                            {
                                var containingType = methodSymbol.ContainingType;
                                if (containingType != null && IsUserDefinedType(containingType))
                                {
                                    var typeLocation = containingType.Locations.FirstOrDefault();
                                    if (typeLocation != null && typeLocation.SourceTree != null)
                                    {
                                        var typeFile = Path.GetFullPath(typeLocation.SourceTree.FilePath);

                                        // 同じファイルはスキップ
                                        if (typeFile == Path.GetFullPath(udonSharpFile))
                                            continue;

                                        if (!typeReferenceGraph.ContainsKey(typeFile))
                                        {
                                            typeReferenceGraph[typeFile] = new HashSet<string>();
                                        }
                                        typeReferenceGraph[typeFile].Add(containingType.Name);
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // セマンティック解析失敗時は無視
                        }
                    }

                    // 静的メソッド呼び出し（InvocationExpression）も検出
                    var invocations = classDecl.DescendantNodes().OfType<InvocationExpressionSyntax>();

                    foreach (var invocation in invocations)
                    {
                        try
                        {
                            var symbolInfo = semanticModel.GetSymbolInfo(invocation);

                            if (symbolInfo.Symbol is IMethodSymbol methodSymbol && methodSymbol.IsStatic)
                            {
                                var containingType = methodSymbol.ContainingType;
                                if (containingType != null && IsUserDefinedType(containingType))
                                {
                                    var typeLocation = containingType.Locations.FirstOrDefault();
                                    if (typeLocation != null && typeLocation.SourceTree != null)
                                    {
                                        var typeFile = Path.GetFullPath(typeLocation.SourceTree.FilePath);

                                        // 同じファイルはスキップ
                                        if (typeFile == Path.GetFullPath(udonSharpFile))
                                            continue;

                                        if (!typeReferenceGraph.ContainsKey(typeFile))
                                        {
                                            typeReferenceGraph[typeFile] = new HashSet<string>();
                                        }
                                        typeReferenceGraph[typeFile].Add(containingType.Name);
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // セマンティック解析失敗時は無視
                        }
                    }
                }
            }

            return typeReferenceGraph;
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

        /// <summary>
        /// UdonSharp制約: SendCustomEvent系メソッドで指定したメソッド名が存在するか検証
        ///
        /// SendCustomEvent、SendCustomEventDelayedSeconds、SendCustomEventDelayedFrames、
        /// SendCustomNetworkEventで指定した文字列のメソッドが存在しない場合、
        /// 実行時エラーとなります。このチェックでは、タイポやリファクタリング時の
        /// メソッド名変更漏れを検出します。
        ///
        /// 例:
        /// NG: SendCustomEvent("OnDamege"); // "OnDamage"のタイポ
        /// NG: SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "OnPlayerJoind"); // タイポ
        /// OK: SendCustomEvent("OnDamage"); public void OnDamage() { }
        /// </summary>
        private static void CheckSendCustomEventMethods(SyntaxNode root, string filePath, List<LintError> errors, CSharpCompilation compilation)
        {
            var tree = root.SyntaxTree;
            var semanticModel = compilation.GetSemanticModel(tree);

            if (semanticModel == null)
                return;

            // SendCustomEvent系メソッド名
            var sendCustomEventMethods = new HashSet<string>
            {
                "SendCustomEvent",
                "SendCustomEventDelayedSeconds",
                "SendCustomEventDelayedFrames",
                "SendCustomNetworkEvent"
            };

            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                try
                {
                    string methodName = "";

                    // メソッド名を取得
                    if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                    {
                        methodName = memberAccess.Name.Identifier.Text;
                    }
                    else if (invocation.Expression is IdentifierNameSyntax identifier)
                    {
                        methodName = identifier.Identifier.Text;
                    }

                    if (!sendCustomEventMethods.Contains(methodName))
                        continue;

                    // 引数からイベント名を取得
                    var args = invocation.ArgumentList.Arguments;
                    if (args.Count == 0)
                        continue;

                    // SendCustomNetworkEventの場合、2番目の引数がイベント名
                    int eventNameArgIndex = methodName == "SendCustomNetworkEvent" ? 1 : 0;
                    if (args.Count <= eventNameArgIndex)
                        continue;

                    var eventNameArg = args[eventNameArgIndex].Expression;

                    // 文字列リテラルの場合のみチェック
                    if (eventNameArg is LiteralExpressionSyntax literal &&
                        literal.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        var eventName = literal.Token.ValueText;

                        // 呼び出し元のクラスを特定
                        ClassDeclarationSyntax? targetClass = null;

                        if (invocation.Expression is MemberAccessExpressionSyntax ma)
                        {
                            // someObject.SendCustomEvent() の場合
                            var receiverSymbol = semanticModel.GetSymbolInfo(ma.Expression).Symbol;

                            if (receiverSymbol is ILocalSymbol localSymbol)
                            {
                                targetClass = FindClassDeclarationForType(root, localSymbol.Type, compilation);
                            }
                            else if (receiverSymbol is IFieldSymbol fieldSymbol)
                            {
                                targetClass = FindClassDeclarationForType(root, fieldSymbol.Type, compilation);
                            }
                            else if (receiverSymbol is IParameterSymbol paramSymbol)
                            {
                                targetClass = FindClassDeclarationForType(root, paramSymbol.Type, compilation);
                            }
                            else if (ma.Expression is ThisExpressionSyntax)
                            {
                                // this.SendCustomEvent() の場合
                                targetClass = invocation.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                            }
                        }
                        else
                        {
                            // SendCustomEvent() の場合 (暗黙のthis)
                            targetClass = invocation.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                        }

                        if (targetClass != null)
                        {
                            // メソッドが存在するかチェック
                            var methods = targetClass.Members.OfType<MethodDeclarationSyntax>();
                            bool methodExists = methods.Any(m => m.Identifier.Text == eventName);

                            if (!methodExists)
                            {
                                AddError(
                                    errors,
                                    filePath,
                                    invocation,
                                    $"Method '{eventName}' not found in class '{targetClass.Identifier.Text}'. " +
                                    $"SendCustomEvent will fail at runtime if the method does not exist.",
                                    LintErrorCodes.SendCustomEventMethodNotFound
                                );
                            }
                        }
                    }
                }
                catch
                {
                    // セマンティック解析が失敗した場合は無視
                }
            }
        }

        /// <summary>
        /// 型に対応するクラス宣言を検索
        /// </summary>
        private static ClassDeclarationSyntax? FindClassDeclarationForType(SyntaxNode root, ITypeSymbol typeSymbol, CSharpCompilation compilation)
        {
            if (typeSymbol == null)
                return null;

            // まず現在のファイル内で検索
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            foreach (var classDecl in classes)
            {
                if (classDecl.Identifier.Text == typeSymbol.Name)
                {
                    return classDecl;
                }
            }

            // 他のファイルも検索
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (tree == root.SyntaxTree)
                    continue;

                var treeRoot = tree.GetRoot();
                var treeClasses = treeRoot.DescendantNodes().OfType<ClassDeclarationSyntax>();
                foreach (var classDecl in treeClasses)
                {
                    if (classDecl.Identifier.Text == typeSymbol.Name)
                    {
                        return classDecl;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// UdonSharp制約: null条件演算子 (?.) は使用できません
        ///
        /// Udonではnull条件演算子（?.）がサポートされていません。
        /// 代わりに、明示的なnullチェックを使用する必要があります。
        ///
        /// 例:
        /// NG: player?.GetDisplayName();
        /// NG: player?.health;
        /// OK: if (player != null) player.GetDisplayName();
        /// OK: player != null ? player.health : 0;
        /// </summary>
        private static void CheckNullConditionalOperators(SyntaxNode root, string filePath, List<LintError> errors)
        {
            // ?. 演算子 (ConditionalAccessExpression)
            var conditionalAccesses = root.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>();

            foreach (var conditionalAccess in conditionalAccesses)
            {
                AddError(errors, filePath, conditionalAccess,
                    "Null conditional operator (?.) is not supported in UdonSharp. Use explicit null checks instead.",
                    LintErrorCodes.NullConditionalOperator);
            }
        }

        /// <summary>
        /// [DEPRECATED - UdonSharpでサポート] null合体演算子 (??, ??=) は使用可能です
        ///
        /// UdonSharpの公式ドキュメントによると、null合体演算子（??）はサポートされています。
        /// 参照: https://udonsharp.docs.vrchat.com/
        ///
        /// 例:
        /// OK: string name = playerName ?? "Guest";
        /// OK: playerName ??= "Guest";
        /// </summary>
        private static void CheckNullCoalescingOperators(SyntaxNode root, string filePath, List<LintError> errors)
        {
            // ?? 演算子 (CoalesceExpression)
            var coalesceExpressions = root.DescendantNodes()
                .OfType<BinaryExpressionSyntax>()
                .Where(b => b.IsKind(SyntaxKind.CoalesceExpression));

            foreach (var coalesce in coalesceExpressions)
            {
                AddError(errors, filePath, coalesce,
                    "Null coalescing operator (??) is not supported in UdonSharp. Use explicit null checks instead.",
                    LintErrorCodes.NullCoalescingOperator);
            }

            // ??= 演算子 (CoalesceAssignmentExpression)
            var coalesceAssignments = root.DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Where(a => a.IsKind(SyntaxKind.CoalesceAssignmentExpression));

            foreach (var coalesceAssignment in coalesceAssignments)
            {
                AddError(errors, filePath, coalesceAssignment,
                    "Null coalescing assignment operator (??=) is not supported in UdonSharp. Use explicit null checks instead.",
                    LintErrorCodes.NullCoalescingOperator);
            }
        }

        /// <summary>
        /// UdonSharp制約: async/await は使用できません
        ///
        /// Udonでは非同期処理（async/await）がサポートされていません。
        /// 代わりに、SendCustomEventDelayedSecondsやUpdateループを使用して
        /// 非同期的な処理を実装する必要があります。
        ///
        /// 例:
        /// NG: public async void Start() { await Task.Delay(1000); }
        /// NG: public async Task&lt;int&gt; GetValueAsync() { }
        /// OK: public void Start() { SendCustomEventDelayedSeconds("DelayedAction", 1.0f); }
        /// </summary>
        private static void CheckAsyncAwait(SyntaxNode root, string filePath, List<LintError> errors)
        {
            // async メソッド
            var asyncMethods = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.AsyncKeyword)));

            foreach (var method in asyncMethods)
            {
                AddError(errors, filePath, method,
                    $"Async methods are not supported in UdonSharp. Use SendCustomEventDelayedSeconds or Update loop instead.",
                    LintErrorCodes.AsyncAwait);
            }

            // async ローカル関数（LocalFunctionで既にエラーになるが、より具体的なメッセージを出す）
            var asyncLocalFunctions = root.DescendantNodes()
                .OfType<LocalFunctionStatementSyntax>()
                .Where(f => f.Modifiers.Any(mod => mod.IsKind(SyntaxKind.AsyncKeyword)));

            foreach (var localFunc in asyncLocalFunctions)
            {
                AddError(errors, filePath, localFunc,
                    "Async local functions are not supported in UdonSharp.",
                    LintErrorCodes.AsyncAwait);
            }

            // await 式
            var awaitExpressions = root.DescendantNodes().OfType<AwaitExpressionSyntax>();

            foreach (var awaitExpr in awaitExpressions)
            {
                AddError(errors, filePath, awaitExpr,
                    "Await expressions are not supported in UdonSharp.",
                    LintErrorCodes.AsyncAwait);
            }
        }

        /// <summary>
        /// UdonSharp制約: goto文およびラベル文は使用できません
        ///
        /// Udonではgoto文（goto label、goto case、goto default）および
        /// ラベル付き文がサポートされていません。
        /// 代わりに、ループのbreak/continueや、メソッド分割を使用してください。
        ///
        /// 例:
        /// NG: goto retry;
        /// NG: retry: DoSomething();
        /// NG: switch (x) { case 1: goto case 2; }
        /// OK: while (shouldRetry) { DoSomething(); }
        /// </summary>
        private static void CheckGotoStatements(SyntaxNode root, string filePath, List<LintError> errors)
        {
            // goto 文
            var gotoStatements = root.DescendantNodes().OfType<GotoStatementSyntax>();

            foreach (var gotoStmt in gotoStatements)
            {
                string gotoType = gotoStmt.Kind() switch
                {
                    SyntaxKind.GotoCaseStatement => "goto case",
                    SyntaxKind.GotoDefaultStatement => "goto default",
                    _ => "goto"
                };

                AddError(errors, filePath, gotoStmt,
                    $"'{gotoType}' statements are not supported in UdonSharp. Use loops with break/continue or method extraction instead.",
                    LintErrorCodes.GotoStatement);
            }

            // ラベル付き文
            var labeledStatements = root.DescendantNodes().OfType<LabeledStatementSyntax>();

            foreach (var labeledStmt in labeledStatements)
            {
                AddError(errors, filePath, labeledStmt,
                    $"Labeled statements ('{labeledStmt.Identifier.Text}:') are not supported in UdonSharp.",
                    LintErrorCodes.GotoStatement);
            }
        }

        /// <summary>
        /// UdonSharp制約: ユーザー定義型の静的フィールドへのアクセスは使用できません
        ///
        /// UdonSharpでは、ユーザー定義型（UdonSharpBehaviourを継承していない通常のクラス）の
        /// 静的フィールドにアクセスすることができません。これは、Udonの実行環境が
        /// 静的な状態の共有をサポートしていないためです。
        ///
        /// ただし、以下は許可されています：
        /// - const（コンパイル時定数）
        /// - Unity組み込み型の静的フィールド（UnityEngine、VRC.SDKBase等）
        ///
        /// 例:
        /// NG: var c = RybColorUtility.SomeColor; // ユーザー定義型の静的フィールド
        /// NG: MyHelper.Counter++; // ユーザー定義型の静的フィールド
        /// OK: var pi = Mathf.PI; // Unity組み込み型の静的フィールド
        /// OK: var max = int.MaxValue; // System型の静的フィールド
        /// OK: var c = MyClass.MY_CONST; // const は OK
        /// </summary>
        private static void CheckUserDefinedTypeStaticFieldAccess(SyntaxNode root, string filePath, List<LintError> errors, CSharpCompilation compilation)
        {
            var tree = root.SyntaxTree;
            var semanticModel = compilation.GetSemanticModel(tree);

            if (semanticModel == null)
                return;

            // UdonSharpBehaviourクラス内のコードのみをチェック
            var udonSharpClasses = FindUdonSharpBehaviourClasses(root);

            foreach (var classDecl in udonSharpClasses)
            {
                var memberAccesses = classDecl.DescendantNodes().OfType<MemberAccessExpressionSyntax>();

                foreach (var memberAccess in memberAccesses)
                {
                    try
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);

                        if (symbolInfo.Symbol is IFieldSymbol fieldSymbol)
                        {
                            // 静的フィールドかどうかをチェック
                            if (!fieldSymbol.IsStatic)
                                continue;

                            // constは許可
                            if (fieldSymbol.IsConst)
                                continue;

                            // static readonlyもconstと同様に扱う（コンパイル時または静的初期化時に決定される値）
                            if (fieldSymbol.IsReadOnly)
                                continue;

                            var containingType = fieldSymbol.ContainingType;

                            if (containingType == null)
                                continue;

                            // ユーザー定義型かどうかを判定
                            if (IsUserDefinedType(containingType))
                            {
                                AddError(
                                    errors,
                                    filePath,
                                    memberAccess,
                                    $"Static fields on user-defined types are not supported in UdonSharp. " +
                                    $"Field '{fieldSymbol.Name}' on type '{containingType.Name}' is a static field. " +
                                    $"Use const instead, or move the field to a UdonSharpBehaviour with [UdonSynced] if synchronization is needed.",
                                    LintErrorCodes.UserDefinedTypeStaticFieldAccess
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

        /// <summary>
        /// UdonSharp制約: UdonSharpから参照されるユーザー定義型内の静的フィールド定義は使用できません
        ///
        /// UdonSharpから参照されるユーティリティクラス（UdonSharpBehaviourを継承していない）内で
        /// 静的フィールドを定義することはできません。UdonSharpコンパイラはこれらのファイルも
        /// 解析するため、静的フィールドがあるとコンパイルエラーになります。
        ///
        /// ただし、以下は許可されています：
        /// - const（コンパイル時定数）
        /// - static readonly
        ///
        /// 例:
        /// NG: public class RybColorUtility { public static Color SomeColor; }
        /// OK: public class RybColorUtility { public const int MAX_VALUE = 100; }
        /// OK: public class RybColorUtility { public static readonly Color DefaultColor = Color.white; }
        /// </summary>
        private static void CheckReferencedTypeStaticFields(SyntaxNode root, string filePath, List<LintError> errors, HashSet<string> referencedTypes)
        {
            if (referencedTypes == null || referencedTypes.Count == 0)
                return;

            // このファイル内のすべてのクラス宣言を取得
            var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

            foreach (var classDecl in classDeclarations)
            {
                // UdonSharpから参照される型のみをチェック
                if (!referencedTypes.Contains(classDecl.Identifier.Text))
                    continue;

                // UdonSharpBehaviourを継承しているクラスはスキップ（既存のCheckStaticFieldsでチェック済み）
                if (IsUdonSharpBehaviourClass(classDecl))
                    continue;

                // 静的フィールドをチェック
                var staticFields = classDecl.Members.OfType<FieldDeclarationSyntax>()
                    .Where(f => f.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)) &&
                               !f.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword)) &&
                               !f.Modifiers.Any(m => m.IsKind(SyntaxKind.ReadOnlyKeyword)));

                foreach (var field in staticFields)
                {
                    foreach (var variable in field.Declaration.Variables)
                    {
                        AddError(
                            errors,
                            filePath,
                            variable,
                            $"Static fields on user-defined types are not supported in UdonSharp. " +
                            $"Field '{variable.Identifier.Text}' in class '{classDecl.Identifier.Text}' is referenced from UdonSharp. " +
                            $"Use const or static readonly instead.",
                            LintErrorCodes.UserDefinedTypeStaticFieldAccess
                        );
                    }
                }
            }
        }

        private static readonly HashSet<string> BannedGenericCollectionNames = new HashSet<string> { "List", "Dictionary", "HashSet", "Queue", "Stack" };

        /// <summary>
        /// UdonSharp制約: ジェネリックコレクション型は使用できません
        ///
        /// Udonは`List&lt;T&gt;`, `Dictionary&lt;K,V&gt;`, `HashSet&lt;T&gt;`, `Queue&lt;T&gt;`, `Stack&lt;T&gt;`をサポートしていません。
        /// 代わりに配列、またはVRChatが提供する`DataList`/`DataDictionary`を使用してください。
        ///
        /// 例:
        /// NG: private List&lt;int&gt; _scores = new List&lt;int&gt;();
        /// OK: private int[] _scores;
        /// OK: private DataList _scores = new DataList();
        /// </summary>
        private static void CheckGenericCollectionTypes(SyntaxNode root, string filePath, List<LintError> errors)
        {
            var genericNames = root.DescendantNodes().OfType<GenericNameSyntax>()
                .Where(g => BannedGenericCollectionNames.Contains(g.Identifier.Text));

            foreach (var generic in genericNames)
            {
                AddError(errors, filePath, generic,
                    $"Generic collection type '{generic.Identifier.Text}<T>' is not supported in UdonSharp. " +
                    $"Use an array or VRChat's DataList/DataDictionary instead.",
                    LintErrorCodes.GenericCollectionType);
            }
        }

        private const string LinqUsageMessage = "LINQ (System.Linq) is not supported in UdonSharp. Use a manual loop over the array instead.";

        /// <summary>
        /// UdonSharp制約: LINQは使用できません
        ///
        /// UdonはSystem.Linq名前空間（`.Where`, `.Select`など）をサポートしていません。
        /// 配列に対する手動のforループで代替してください。
        ///
        /// 例:
        /// NG: using System.Linq;
        /// OK: foreach (var item in items) { if (condition) { ... } }
        /// </summary>
        private static void CheckLinqUsage(SyntaxNode root, string filePath, List<LintError> errors, CSharpCompilation compilation)
        {
            // テキスト上の完全修飾呼び出しに加え、セマンティックモデルでSystem.Linqの拡張メソッドとして
            // 解決される呼び出し(namespace alias, global using, 修飾なしの拡張メソッド構文など)も検出する
            var semanticModel = compilation.GetSemanticModel(root.SyntaxTree);

            foreach (var node in root.DescendantNodes())
            {
                if (node is UsingDirectiveSyntax usingDirective && usingDirective.Name != null &&
                    IsLinqNamespaceText(usingDirective.Name.ToString()))
                {
                    AddError(errors, filePath, usingDirective, LinqUsageMessage, LintErrorCodes.LinqUsage);
                }
                else if (node is InvocationExpressionSyntax invocation &&
                    (IsLinqNamespaceText(invocation.Expression.ToString()) ||
                     ResolvesToLinqMethod(invocation, semanticModel)))
                {
                    AddError(errors, filePath, invocation, LinqUsageMessage, LintErrorCodes.LinqUsage);
                }
            }
        }

        /// <summary>
        /// 呼び出しがSystem.Linqのメソッドとして解決されるか判定する。
        /// オーバーロード解決の曖昧さ等でSymbolがnullになる場合は、CandidateSymbolsもフォールバックとして確認する
        /// (例: LINQのWhereと同名・同シグネチャの拡張メソッドが別の名前空間にも存在する場合)
        /// </summary>
        private static bool ResolvesToLinqMethod(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            IEnumerable<ISymbol> candidates = symbolInfo.Symbol != null
                ? new ISymbol[] { symbolInfo.Symbol }
                : symbolInfo.CandidateSymbols;

            return candidates.OfType<IMethodSymbol>()
                .Any(m => m.ContainingType?.ContainingNamespace?.ToDisplayString() == "System.Linq");
        }

        /// <summary>
        /// 名前空間参照テキストがSystem.Linq(のサブ名前空間)を指しているか判定する。
        /// global::プレフィックスを許容し、System.Linq.Expressions等の無関係な名前空間は除外する。
        /// </summary>
        private static bool IsLinqNamespaceText(string namespaceText)
        {
            const string globalPrefix = "global::";
            var normalized = namespaceText.StartsWith(globalPrefix)
                ? namespaceText.Substring(globalPrefix.Length)
                : namespaceText;

            return (normalized == "System.Linq" || normalized.StartsWith("System.Linq.")) &&
                   normalized != "System.Linq.Expressions" &&
                   !normalized.StartsWith("System.Linq.Expressions.");
        }

        /// <summary>
        /// UdonSharp制約: ラムダ式、delegate、C#イベントは使用できません
        ///
        /// Udonはラムダ式(`=&gt;`)、`delegate`宣言、C#標準の`event`キーワードをサポートしていません。
        /// 名前付きのprivateメソッドや、UdonSharpのSendCustomEvent系APIで代替してください。
        ///
        /// 例:
        /// NG: items.ForEach(x =&gt; DoSomething(x));
        /// NG: public delegate void MyDelegate();
        /// NG: public event Action OnSomething;
        /// OK: private void DoSomething(int x) { ... }
        /// </summary>
        private static void CheckLambdaAndDelegates(SyntaxNode root, string filePath, List<LintError> errors)
        {
            foreach (var node in root.DescendantNodes())
            {
                switch (node)
                {
                    case SimpleLambdaExpressionSyntax _:
                    case ParenthesizedLambdaExpressionSyntax _:
                    case AnonymousMethodExpressionSyntax _:
                        AddError(errors, filePath, node,
                            "Lambda expressions are not supported in UdonSharp. Use a named private method instead.",
                            LintErrorCodes.LambdaOrDelegate);
                        break;
                    case DelegateDeclarationSyntax _:
                        AddError(errors, filePath, node,
                            "Delegate declarations are not supported in UdonSharp.",
                            LintErrorCodes.LambdaOrDelegate);
                        break;
                    case EventDeclarationSyntax _:
                    case EventFieldDeclarationSyntax _:
                        AddError(errors, filePath, node,
                            "C# events are not supported in UdonSharp. Use SendCustomEvent/SendCustomNetworkEvent instead.",
                            LintErrorCodes.LambdaOrDelegate);
                        break;
                }
            }
        }

        /// <summary>
        /// UdonSharp制約: コルーチン（yield return / StartCoroutine）は使用できません
        ///
        /// Udonは標準のUnityコルーチン機構をサポートしていません。
        /// 時間差実行が必要な場合は、SendCustomEventDelayedSeconds/SendCustomEventDelayedFramesを使用してください。
        ///
        /// 例:
        /// NG: yield return new WaitForSeconds(1f);
        /// NG: StartCoroutine(MyCoroutine());
        /// OK: SendCustomEventDelayedSeconds(nameof(MyMethod), 1f);
        /// </summary>
        private static void CheckCoroutineUsage(SyntaxNode root, string filePath, List<LintError> errors)
        {
            const string message = "Coroutines are not supported in UdonSharp. Use SendCustomEventDelayedSeconds/SendCustomEventDelayedFrames instead.";

            foreach (var node in root.DescendantNodes())
            {
                if (node is YieldStatementSyntax)
                {
                    AddError(errors, filePath, node, message, LintErrorCodes.CoroutineUsage);
                }
                else if (node is InvocationExpressionSyntax invocation &&
                    (IsInvocationOf(invocation, "StartCoroutine") ||
                     IsInvocationOf(invocation, "StopCoroutine") ||
                     IsInvocationOf(invocation, "StopAllCoroutines")))
                {
                    AddError(errors, filePath, invocation, message, LintErrorCodes.CoroutineUsage);
                }
            }
        }

        /// <summary>
        /// UdonSharp制約: UnityEvent.AddListener()によるランタイム登録は使用できません
        ///
        /// `Button.onClick.AddListener()`のようなコードからのイベントハンドラ登録はUdonSharpのメソッドを
        /// 正しく呼び出せません。Unityエディタのインスペクターから、対象のUdonBehaviourの
        /// SendCustomEventを呼び出すよう設定してください。
        ///
        /// 例:
        /// NG: button.onClick.AddListener(OnButtonClick);
        /// OK: インスペクターのOnClickイベントでUdonBehaviour.SendCustomEventを設定する
        /// </summary>
        private static void CheckUIEventListenerRegistration(SyntaxNode root, string filePath, List<LintError> errors)
        {
            // UnityEventのフィールド命名規則(onClick, onValueChangedなど)に一致するレシーバー経由の
            // AddListener呼び出しのみを対象にし、無関係な独自メソッド(例: manager.AddListener(id))への
            // 誤検知を避ける。レシーバーは`button.onClick`のようなメンバーアクセスだけでなく、
            // `onReady`のようにUnityEventが直接フィールド/プロパティとして公開されている場合も対象にする
            var addListenerInvocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(inv => IsInvocationOf(inv, "AddListener") &&
                              inv.Expression is MemberAccessExpressionSyntax member &&
                              IsUnityEventLikeReceiver(member.Expression));

            foreach (var invocation in addListenerInvocations)
            {
                AddError(errors, filePath, invocation,
                    "UnityEvent.AddListener() cannot reliably register UdonSharp methods at runtime. " +
                    "Wire the event to a SendCustomEvent call from the Inspector instead.",
                    LintErrorCodes.UIEventListenerRegistration);
            }
        }

        /// <summary>
        /// UdonSharp制約: GetComponent&lt;UdonBehaviour&gt;()は使用できません
        ///
        /// 低レベルの`UdonBehaviour`型に対するジェネリック版GetComponentは非対応です。
        /// `(UdonBehaviour)GetComponent(typeof(UdonBehaviour))`を使用してください。
        /// なお、UdonSharpBehaviourを継承したクラスに対するGetComponent&lt;T&gt;()はSDK 3.8+でサポートされています。
        ///
        /// 例:
        /// NG: var udon = GetComponent&lt;UdonBehaviour&gt;();
        /// OK: var udon = (UdonBehaviour)GetComponent(typeof(UdonBehaviour));
        /// </summary>
        private static void CheckGenericGetComponentUdonBehaviour(SyntaxNode root, string filePath, List<LintError> errors)
        {
            var genericGetComponents = root.DescendantNodes().OfType<GenericNameSyntax>()
                .Where(g => g.Identifier.Text == "GetComponent" &&
                            g.TypeArgumentList.Arguments.Count == 1 &&
                            GetRightmostTypeName(g.TypeArgumentList.Arguments[0]) == "UdonBehaviour");

            foreach (var generic in genericGetComponents)
            {
                AddError(errors, filePath, generic,
                    "GetComponent<UdonBehaviour>() is not supported. Use (UdonBehaviour)GetComponent(typeof(UdonBehaviour)) instead.",
                    LintErrorCodes.GenericGetComponentUdonBehaviour, DiagnosticSeverity.Warning);
            }
        }

        /// <summary>
        /// 型テキストの最後のドット区切りセグメントを取得する(例: "global::VRC.Udon.UdonBehaviour" -&gt; "UdonBehaviour")
        /// </summary>
        private static string GetRightmostTypeName(TypeSyntax type)
        {
            return GetRightmostDottedSegment(type.ToString());
        }

        /// <summary>
        /// テキストの最後のドット区切りセグメントを取得する(例: "BehaviourSyncMode.None" -&gt; "None")
        /// </summary>
        private static string GetRightmostDottedSegment(string text)
        {
            var lastDotIndex = text.LastIndexOf('.');
            return lastDotIndex >= 0 ? text.Substring(lastDotIndex + 1) : text;
        }

        /// <summary>
        /// UdonSharp制約: ネットワーク同期変数([UdonSynced])の設定ミスや非効率なパターンを検出します
        ///
        /// 以下を検証します：
        /// - NoVariableSync（同期なし）モードのビヘイビアに[UdonSynced]フィールドがある（設定の矛盾）
        /// - Manual同期モードで[UdonSynced]フィールドがあるのにRequestSerialization()が一度も呼ばれていない
        /// - 1つのビヘイビアあたりの[UdonSynced]フィールド数が多すぎる（ネットワーク帯域の目安を超過）
        /// - int[]/float[]型の同期変数（byte[]/short[]等よりも多くの帯域を消費する）
        ///
        /// 例:
        /// NG: [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]付きクラスに[UdonSynced]フィールドがある
        /// NG: Manual同期でRequestSerialization()を一度も呼ばない
        /// OK: 同期変数は必要最小限に絞り、Manual同期では変更後にRequestSerialization()を呼ぶ
        /// </summary>
        private static void CheckSynchronizationConstraints(SyntaxNode root, string filePath, List<LintError> errors)
        {
            var classes = FindUdonSharpBehaviourClasses(root);

            foreach (var classDecl in classes)
            {
                var syncMode = GetUdonBehaviourSyncMode(classDecl);
                var syncedFields = classDecl.Members.OfType<FieldDeclarationSyntax>()
                    .Where(f => HasAttribute(f, "UdonSynced"))
                    .ToList();

                CheckSyncModeConflict(syncMode, syncedFields, filePath, errors);
                CheckManualSyncMissingRequestSerialization(classDecl, syncMode, syncedFields, filePath, errors);
                CheckExcessiveSyncedVariables(classDecl, syncedFields, filePath, errors);
                CheckLargeArraySynced(syncedFields, filePath, errors);
            }
        }

        /// <summary>
        /// syncModeテキストがNone/NoVariableSync(同期変数を禁止するモード)を指しているか判定する。
        /// 完全修飾名(BehaviourSyncMode.None)、using static等による裸名(None)の両方を許容する。
        /// </summary>
        private static bool IsNoSyncMode(string syncMode)
        {
            var bareName = GetRightmostDottedSegment(syncMode);
            return bareName == "None" || bareName == "NoVariableSync";
        }

        private static void CheckSyncModeConflict(string syncMode,
            List<FieldDeclarationSyntax> syncedFields, string filePath, List<LintError> errors)
        {
            if (syncMode == null || !IsNoSyncMode(syncMode))
                return;

            foreach (var field in syncedFields)
            {
                foreach (var variable in field.Declaration.Variables)
                {
                    AddError(errors, filePath, variable,
                        $"[UdonSynced] field found in a class with [UdonBehaviourSyncMode({syncMode})], which forbids synced " +
                        "fields entirely. Synced fields require Continuous or Manual sync mode.",
                        LintErrorCodes.SyncModeConflict);
                }
            }
        }

        private static void CheckManualSyncMissingRequestSerialization(ClassDeclarationSyntax classDecl, string syncMode,
            List<FieldDeclarationSyntax> syncedFields, string filePath, List<LintError> errors)
        {
            if (syncMode == null || !syncMode.Contains("Manual") || !syncedFields.Any())
                return;

            bool callsRequestSerialization = classDecl.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Any(inv => IsInvocationOf(inv, "RequestSerialization") && IsSelfReceiverInvocation(inv));

            if (!callsRequestSerialization)
            {
                AddError(errors, filePath, syncedFields.First(),
                    "Manual sync mode with [UdonSynced] fields requires an explicit RequestSerialization() call to send updates to other players.",
                    LintErrorCodes.ManualSyncMissingRequestSerialization, DiagnosticSeverity.Warning);
            }
        }

        private const int RecommendedMaxSyncedFieldCount = 8;

        private static void CheckExcessiveSyncedVariables(ClassDeclarationSyntax classDecl,
            List<FieldDeclarationSyntax> syncedFields, string filePath, List<LintError> errors)
        {
            // 1つのFieldDeclarationSyntaxに複数変数が含まれる場合(例: [UdonSynced] int a, b, c;)があるため、
            // 宣言数ではなく変数数を数える
            int syncedVariableCount = syncedFields.Sum(f => f.Declaration.Variables.Count);

            if (syncedVariableCount > RecommendedMaxSyncedFieldCount)
            {
                AddError(errors, filePath, classDecl,
                    $"This behaviour has {syncedVariableCount} [UdonSynced] variables. Consider keeping synced data under " +
                    $"~{RecommendedMaxSyncedFieldCount} variables per behaviour to reduce network bandwidth usage.",
                    LintErrorCodes.ExcessiveSyncedVariables, DiagnosticSeverity.Warning);
            }
        }

        private static readonly HashSet<string> LargeSyncedArrayElementTypes = new HashSet<string>
        {
            "int", "Int32", "System.Int32",
            "float", "Single", "System.Single"
        };

        private static void CheckLargeArraySynced(List<FieldDeclarationSyntax> syncedFields, string filePath, List<LintError> errors)
        {
            foreach (var field in syncedFields)
            {
                if (!(field.Declaration.Type is ArrayTypeSyntax arrayType) ||
                    !LargeSyncedArrayElementTypes.Contains(arrayType.ElementType.ToString()))
                {
                    continue;
                }

                foreach (var variable in field.Declaration.Variables)
                {
                    AddError(errors, filePath, variable,
                        $"Synced array of type '{arrayType.ElementType}[]' uses more network bandwidth than necessary. " +
                        $"Consider 'byte[]' or 'short[]' if the value range allows it.",
                        LintErrorCodes.LargeArraySynced, DiagnosticSeverity.Warning);
                }
            }
        }

        /// <summary>
        /// ユーザー定義型かどうかを判定
        /// Unity/VRC/System組み込み型を除外
        /// </summary>
        private static bool IsUserDefinedType(INamedTypeSymbol typeSymbol)
        {
            if (typeSymbol == null)
                return false;

            var namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "";

            // 組み込み名前空間を除外
            var builtInNamespaces = new[]
            {
                "UnityEngine",
                "UnityEditor",
                "VRC",
                "UdonSharp",
                "TMPro",
                "System",
                "Microsoft"
            };

            foreach (var ns in builtInNamespaces)
            {
                if (namespaceName.StartsWith(ns))
                {
                    return false;
                }
            }

            // グローバル名前空間の場合、型名で判断
            if (string.IsNullOrEmpty(namespaceName))
            {
                // Unity組み込み型のリスト（グローバル名前空間にある場合）
                var unityBuiltInTypes = new HashSet<string>
                {
                    "Mathf", "Vector2", "Vector3", "Vector4", "Color", "Color32",
                    "Quaternion", "Matrix4x4", "Bounds", "Rect", "Ray", "Plane"
                };

                if (unityBuiltInTypes.Contains(typeSymbol.Name))
                {
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region Test Helpers

        /// <summary>
        /// Analyzes source code and returns lint errors (for testing purposes)
        /// </summary>
        internal static List<LintError> AnalyzeCode(string sourceCode, string filePath = "TestFile.cs")
        {
            var tree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            var root = tree.GetRoot();

            var references = GetTrustedPlatformAssemblyReferences();

            var compilation = CSharpCompilation.Create(
                "TestCompilation",
                new[] { tree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            var errors = new List<LintError>();

            // Run all syntax checks
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
            // Note: Properties are now supported in UdonSharp 1.0+
            // CheckProperties(root, filePath, errors);
            CheckMethodOverloads(root, filePath, errors);
            CheckInterfaces(root, filePath, errors);
            CheckCrossFileFieldAccess(root, filePath, errors, compilation);
            CheckCrossFileMethodInvocation(root, filePath, errors, compilation);
            CheckUdonBehaviourSerializableClassUsage(root, filePath, errors, compilation);
            CheckSendCustomEventMethods(root, filePath, errors, compilation);
            CheckNullConditionalOperators(root, filePath, errors);
            // Note: Null coalescing operator (??) is now supported in UdonSharp
            // CheckNullCoalescingOperators(root, filePath, errors);
            CheckAsyncAwait(root, filePath, errors);
            CheckGotoStatements(root, filePath, errors);
            CheckUserDefinedTypeStaticFieldAccess(root, filePath, errors, compilation);
            CheckGenericCollectionTypes(root, filePath, errors);
            CheckLinqUsage(root, filePath, errors, compilation);
            CheckLambdaAndDelegates(root, filePath, errors);
            CheckCoroutineUsage(root, filePath, errors);
            CheckUIEventListenerRegistration(root, filePath, errors);
            CheckGenericGetComponentUdonBehaviour(root, filePath, errors);
            CheckSynchronizationConstraints(root, filePath, errors);

            return errors;
        }

        /// <summary>
        /// Analyzes multiple in-memory source files as a single compilation and returns lint errors
        /// across all of them (for testing purposes). Mirrors the multi-file pipeline in Main():
        /// per-UdonSharp-file syntax/semantic checks, plus the call-graph-driven static-method-file
        /// check (CheckStaticMethodFieldAccess) and the type-reference-graph-driven referenced-type-file
        /// check (CheckReferencedTypeStaticFields), so cross-file semantic checks are testable.
        /// </summary>
        internal static List<LintError> AnalyzeCodeMultiFile(params (string path, string source)[] files)
        {
            return AnalyzeCodeMultiFile(files, scriptAssembliesDirOverride: null);
        }

        /// <summary>
        /// Overload of <see cref="AnalyzeCodeMultiFile(ValueTuple{string, string}[])"/> that lets tests inject a
        /// Library/ScriptAssemblies directory (instead of the CWD-derived one CreateCompilation uses by default),
        /// e.g. to verify behavior when it contains a stale compiled copy of one of the source files under test.
        /// </summary>
        internal static List<LintError> AnalyzeCodeMultiFile((string path, string source)[] files, string? scriptAssembliesDirOverride)
        {
            var syntaxTreeDict = new Dictionary<string, SyntaxTree>();
            var rawPaths = new Dictionary<string, string>();

            foreach (var (path, source) in files)
            {
                var tree = CSharpSyntaxTree.ParseText(source, path: path);
                var normalizedPath = Path.GetFullPath(path);
                syntaxTreeDict[normalizedPath] = tree;
                rawPaths[normalizedPath] = path;
            }

            var compilation = CreateCompilation(syntaxTreeDict.Values.ToList(), scriptAssembliesDirOverride);

            var udonSharpFiles = syntaxTreeDict
                .Where(kvp => kvp.Value.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Any(IsUdonSharpBehaviourClass))
                .Select(kvp => kvp.Key)
                .ToList();

            var callGraph = BuildCallGraph(compilation, udonSharpFiles);
            var typeReferenceGraph = BuildTypeReferenceGraph(compilation, udonSharpFiles);

            var errors = new List<LintError>();

            foreach (var normalizedPath in udonSharpFiles)
            {
                var root = syntaxTreeDict[normalizedPath].GetRoot();
                var filePath = rawPaths[normalizedPath];

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
                CheckMethodOverloads(root, filePath, errors);
                CheckInterfaces(root, filePath, errors);
                CheckCrossFileFieldAccess(root, filePath, errors, compilation);
                CheckCrossFileMethodInvocation(root, filePath, errors, compilation);
                CheckUdonBehaviourSerializableClassUsage(root, filePath, errors, compilation);
                CheckSendCustomEventMethods(root, filePath, errors, compilation);
                CheckNullConditionalOperators(root, filePath, errors);
                CheckAsyncAwait(root, filePath, errors);
                CheckGotoStatements(root, filePath, errors);
                CheckUserDefinedTypeStaticFieldAccess(root, filePath, errors, compilation);
                CheckGenericCollectionTypes(root, filePath, errors);
                CheckLinqUsage(root, filePath, errors, compilation);
                CheckLambdaAndDelegates(root, filePath, errors);
                CheckCoroutineUsage(root, filePath, errors);
                CheckUIEventListenerRegistration(root, filePath, errors);
                CheckGenericGetComponentUdonBehaviour(root, filePath, errors);
                CheckSynchronizationConstraints(root, filePath, errors);
            }

            foreach (var entry in callGraph)
            {
                if (syntaxTreeDict.TryGetValue(entry.Key, out var tree))
                {
                    CheckStaticMethodFieldAccess(tree.GetRoot(), rawPaths[entry.Key], errors, compilation, entry.Value);
                }
            }

            foreach (var entry in typeReferenceGraph)
            {
                if (syntaxTreeDict.TryGetValue(entry.Key, out var tree))
                {
                    CheckReferencedTypeStaticFields(tree.GetRoot(), rawPaths[entry.Key], errors, entry.Value);
                }
            }

            return errors;
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