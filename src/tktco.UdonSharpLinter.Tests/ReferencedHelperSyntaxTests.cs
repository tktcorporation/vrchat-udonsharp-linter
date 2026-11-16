using System.Linq;
using Xunit;
using tktco.UdonSharpLinter;

namespace tktco.UdonSharpLinter.Tests;

public class ReferencedHelperSyntaxTests
{
    private const string Behaviour = @"
using UdonSharp;
public class Behaviour : UdonSharpBehaviour
{
    public void Start() { Helpers.Run(); }
}";

    [Theory]
    [InlineData("var x = new System.Collections.Generic.List<int>();", 32)]
    [InlineData("var x = System.Linq.Enumerable.Sum(new int[0]);", 33)]
    [InlineData("System.Action x = () => {};", 34)]
    [InlineData("yield return null;", 35)]
    [InlineData("try {} catch {}", 1)]
    [InlineData("throw new System.Exception();", 2)]
    [InlineData("void Local() {}", 3)]
    [InlineData("int[,] x = null;", 9)]
    [InlineData("string x = null; var y = x?.Length;", 27)]
    [InlineData("done: return;", 30)]
    [InlineData("var x = new System.Collections.Generic.List<int> { 1 };", 8)]
    [InlineData("button.onClick.AddListener(Handler);", 36)]
    [InlineData("GetComponent<VRC.Udon.UdonBehaviour>();", 37)]
    public void CalledStaticMethod_RunsCommonChecks(string body, int code)
    {
        var errors = Program.AnalyzeCodeMultiFile(("Behaviour.cs", Behaviour),
            ("Helpers.cs", "public static class Helpers { public static void Run() { " + body + " } }"));
        Assert.Single(errors, e => e.FilePath == "Helpers.cs" && e.Code == code);
    }

    [Fact]
    public void IssueExample_ReportsLinqAndLambdaAtOriginalLocations()
    {
        var errors = Program.AnalyzeCodeMultiFile(("Behaviour.cs", Behaviour.Replace("Helpers.Run()", "Helpers.Run(new int[0])")),
            ("Helpers.cs", @"using System.Linq;
public static class Helpers
{
    public static int Run(int[] values) => values.Where(x => x > 0).Sum();
}"));
        Assert.Single(errors, e => e.FilePath == "Helpers.cs" && e.Code == 33 && e.Line == 4);
        Assert.Single(errors, e => e.FilePath == "Helpers.cs" && e.Code == 34 && e.Line == 4 && e.Column == 57);
    }

    [Fact]
    public void ReferencedFieldType_RunsChecksOnlyOnReferencedType()
    {
        var errors = Program.AnalyzeCodeMultiFile(
            ("Behaviour.cs", @"using UdonSharp; public class Behaviour : UdonSharpBehaviour { public Data data; }"),
            ("Data.cs", @"public class Data { public System.Collections.Generic.List<int> values; }
public class Other { public void Run() { System.Action x = () => {}; } }"));
        Assert.Single(errors, e => e.Code == 32 && e.FilePath == "Data.cs");
        Assert.DoesNotContain(errors, e => e.Code == 34);
    }

    [Fact]
    public void UncalledMethodsAndUnreferencedFiles_AreIgnored()
    {
        var errors = Program.AnalyzeCodeMultiFile(("Behaviour.cs", Behaviour),
            ("Helpers.cs", @"using System.Linq;
public static class Helpers {
 public static void Run() {}
 public static void Unused() { System.Action x = () => {}; }
}
public class Other { public void Run() { throw new System.Exception(); } }"),
            ("Unused.cs", "class Unused { void Run() { try {} catch {} } }"));
        Assert.Empty(errors);
    }

    [Fact]
    public void TransitiveRecursiveCalls_AreCheckedOnce()
    {
        var errors = Program.AnalyzeCodeMultiFile(("Behaviour.cs", Behaviour),
            ("Helpers.cs", "public static class Helpers { public static void Run() { Inner.Run(); Inner.Run(); } }"),
            ("Inner.cs", "public static class Inner { public static void Run() { Helpers.Run(); System.Action a = () => {}; } }"));
        Assert.Single(errors, e => e.Code == 34 && e.FilePath == "Inner.cs");
    }

    [Fact]
    public void TypeAndMethodReachSameCode_FromMultipleBehaviours_NoDuplicateDiagnostics()
    {
        var errors = Program.AnalyzeCodeMultiFile(
            ("Behaviour.cs", Behaviour.Replace("public void Start()", "public Helpers data; public void Start()")),
            ("OtherBehaviour.cs", Behaviour.Replace("class Behaviour", "class OtherBehaviour")),
            ("Helpers.cs", "public class Helpers { public static void Run() { System.Action a = () => {}; } }"));
        Assert.Single(errors, e => e.Code == 34);
        Assert.Equal(errors.Count, errors.Select(e => (e.FilePath, e.Line, e.Column, e.Code, e.Message)).Distinct().Count());
    }

    [Fact]
    public void SameFileUnrelatedType_IsIgnored_ButCalledMethodIsChecked()
    {
        var errors = Program.AnalyzeCodeMultiFile(("Behaviour.cs", Behaviour + @"
public class Unused { public void Run() { throw new System.Exception(); } }
public static class Helpers { public static void Run() { System.Action a = () => {}; } }"));
        Assert.Single(errors, e => e.Code == 34);
        Assert.DoesNotContain(errors, e => e.Code == 2);
    }

    [Fact]
    public void OrdinaryHelper_DoesNotReceiveBehaviourRules()
    {
        var errors = Program.AnalyzeCodeMultiFile(("Behaviour.cs", Behaviour),
            ("Helpers.cs", @"[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class Helpers {
 public Helpers() {}
 [UdonSynced] public int value;
 [NetworkCallable] public static int Run() { return 0; }
}"));
        Assert.Empty(errors);
    }

    [Fact]
    public void GenericCalledMethod_IsChecked()
    {
        var errors = Program.AnalyzeCodeMultiFile(("Behaviour.cs", Behaviour.Replace("Helpers.Run()", "Helpers.Run<int>()")),
            ("Helpers.cs", "public static class Helpers { public static void Run<T>() {} }"));
        Assert.Single(errors, e => e.Code == 6 && e.FilePath == "Helpers.cs");
    }

    [Fact]
    public void PartialAndTransitivelyReferencedTypes_AreChecked()
    {
        var errors = Program.AnalyzeCodeMultiFile(
            ("Behaviour.cs", "using UdonSharp; public class Behaviour : UdonSharpBehaviour { public Data[] data; }"),
            ("Data.cs", "public partial class Data { public Other value; }"),
            ("Data.More.cs", "public partial class Data { public System.Collections.Generic.List<int> items; }"),
            ("Other.cs", "public class Other { public void Run() { System.Action a = () => {}; } }"));
        Assert.Single(errors, e => e.Code == 32 && e.FilePath == "Data.More.cs");
        Assert.Single(errors, e => e.Code == 34 && e.FilePath == "Other.cs");
    }
    [Fact]
    public void GenericExtensionMethodAndOverload_OnlyReachedMethodIsChecked()
    {
        var errors = Program.AnalyzeCodeMultiFile(
            ("Behaviour.cs", Behaviour.Replace("Helpers.Run()", "1.Run<int>()")),
            ("Helpers.cs", @"public static class Helpers {
 public static void Run<T>(this int value) { System.Action a = () => {}; }
 public static void Run(string value) { throw new System.Exception(); }
}"));
        Assert.Single(errors, e => e.Code == 34);
        Assert.Single(errors, e => e.Code == 6);
        Assert.DoesNotContain(errors, e => e.Code == 2);
    }

    [Fact]
    public void CalledAsyncMethod_IsChecked()
    {
        var errors = Program.AnalyzeCodeMultiFile(("Behaviour.cs", Behaviour),
            ("Helpers.cs", @"public static class Helpers {
 public static async System.Threading.Tasks.Task Run() { await System.Threading.Tasks.Task.Delay(1); }
}"));
        Assert.Contains(errors, e => e.Code == 29 && e.FilePath == "Helpers.cs");
    }

    [Fact]
    public void ColocatedOrdinaryHelper_DoesNotReceiveNetworkCallableRules()
    {
        var errors = Program.AnalyzeCodeMultiFile(("Behaviour.cs", Behaviour + @"
public static class Helpers { [NetworkCallable] public static int Run() { return 0; } }"));
        Assert.DoesNotContain(errors, e => e.Code == 13);
    }

    [Theory]
    [InlineData("const")]
    [InlineData("static")]
    public void StaticFieldAccess_DoesNotIncludeUncalledMethods(string modifier)
    {
        var errors = Program.AnalyzeCodeMultiFile(("Behaviour.cs", Behaviour),
            ("Helpers.cs", "public static class Helpers { public " + modifier + @" int Limit = 1;
 public static int Run() { return Helpers.Limit; }
 public static void Unused() { System.Action a = () => {}; }
}"));
        Assert.DoesNotContain(errors, e => e.Code == 34);
    }

    [Fact]
    public void StaticFieldInitializer_IsCheckedWithoutIncludingOtherMethods()
    {
        var errors = Program.AnalyzeCodeMultiFile(("Behaviour.cs", Behaviour),
            ("Helpers.cs", @"public static class Helpers {
 public static System.Action Callback = () => {};
 public static object Run() { return Callback; }
 public static void Unused() { throw new System.Exception(); }
}"));
        Assert.Single(errors, e => e.Code == 34);
        Assert.DoesNotContain(errors, e => e.Code == 2);
    }

    [Fact]
    public void TransitiveStaticPropertyGetter_IsChecked()
    {
        var errors = Program.AnalyzeCodeMultiFile(("Behaviour.cs", Behaviour),
            ("Helpers.cs", "public static class Helpers { public static object Run() => More.Value; }"),
            ("More.cs", @"public static class More {
 public static object Value { get { System.Action a = () => {}; return null; } }
 public static void Unused() { throw new System.Exception(); }
}"));
        Assert.Single(errors, e => e.Code == 34 && e.FilePath == "More.cs");
        Assert.DoesNotContain(errors, e => e.Code == 2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PartialBehaviour_AllDeclarationsAreChecked(bool sameFile)
    {
        const string seed = "using UdonSharp; namespace Active { public partial class B : UdonSharpBehaviour {} }";
        const string part = @"namespace Active { public partial class B {
 public void Start() { throw new System.Exception(); }
} }
namespace Unrelated { public partial class B {
 public void Unused() { System.Action a = () => {}; }
} }";
        var files = sameFile
            ? new[] { ("Behaviour.cs", seed + part) }
            : new[] { ("Behaviour.cs", seed), ("Behaviour.More.cs", part) };

        var errors = Program.AnalyzeCodeMultiFile(files);

        Assert.Single(errors, e => e.Code == 2 && e.FilePath == (sameFile ? "Behaviour.cs" : "Behaviour.More.cs"));
        Assert.DoesNotContain(errors, e => e.Code == 34);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CalledStaticPartialMethod_ImplementationIsChecked(bool sameFile)
    {
        const string definition = "public static partial class Helpers { public static partial void Run(); }";
        const string implementation = @"public static partial class Helpers {
 public static partial void Run() { System.Action a = () => {}; throw new System.Exception(); }
 public static void Run(int value) { done: return; }
 public static void Unused() { done: return; }
}";
        var files = sameFile
            ? new[] { ("Behaviour.cs", Behaviour), ("Helpers.cs", definition + implementation) }
            : new[] { ("Behaviour.cs", Behaviour), ("Helpers.cs", definition), ("Helpers.More.cs", implementation) };

        var errors = Program.AnalyzeCodeMultiFile(files);

        var expectedPath = sameFile ? "Helpers.cs" : "Helpers.More.cs";
        Assert.Single(errors, e => e.Code == 34 && e.FilePath == expectedPath);
        Assert.Single(errors, e => e.Code == 2 && e.FilePath == expectedPath);
        Assert.DoesNotContain(errors, e => e.Code == 30);
    }

    [Fact]
    public void PartialBehaviour_NetworkCallableChecksApplyToAllDeclarations()
    {
        var errors = Program.AnalyzeCodeMultiFile(("Behaviour.cs", @"using UdonSharp;
namespace Active { public partial class B : UdonSharpBehaviour {} }
namespace Active { public partial class B {
 [NetworkCallable] public int Run() => 0;
} }
namespace Unrelated { public partial class B {
 [NetworkCallable] public int OrdinaryCSharpMethod() => 0;
} }"));

        Assert.Single(errors, e => e.Code == 13 && e.Line == 4);
        Assert.DoesNotContain(errors, e => e.Code == 13 && e.Line == 7);
    }

    [Fact]
    public void InheritedBehaviour_NetworkCallableOwnerIsResolvedSemantically()
    {
        var errors = Program.AnalyzeCodeMultiFile(("Behaviour.cs", @"using UdonSharp;
namespace UdonSharp { public class UdonSharpBehaviour {} }
public class BaseBehaviour : UdonSharpBehaviour {}
public class DerivedBehaviour : BaseBehaviour {
 [NetworkCallable] public int Run() => 0;
}
public class Ordinary {
 [NetworkCallable] public int Run() => 0;
}"));

        Assert.Single(errors, e => e.Code == 13 && e.Line == 5);
        Assert.DoesNotContain(errors, e => e.Code == 13 && e.Line == 8);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InheritedBehaviour_CommonChecksAreSeededSemantically(bool includeSdkStub)
    {
        var source = @"using UdonSharp;
public class BaseBehaviour : UdonSharpBehaviour {}
public class DerivedBehaviour : BaseBehaviour {
 public void Start() { throw new System.Exception(); }
}
public class Ordinary {
 public void Unused() { System.Action a = () => {}; }
}";
        if (includeSdkStub)
            source += "namespace UdonSharp { public class UdonSharpBehaviour {} }";

        var errors = Program.AnalyzeCodeMultiFile(("Behaviour.cs", source));

        Assert.Single(errors, e => e.Code == 2 && e.Line == 4);
        Assert.DoesNotContain(errors, e => e.Code == 34);
    }

}
