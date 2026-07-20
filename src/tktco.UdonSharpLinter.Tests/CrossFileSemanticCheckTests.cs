using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using tktco.UdonSharpLinter;

namespace tktco.UdonSharpLinter.Tests;

/// <summary>
/// Regression tests for the semantic-model checks that require multiple syntax trees in one
/// compilation (cross-file field/method access, static-method-file and referenced-type-file
/// checks). These previously had zero automated coverage (#31).
/// </summary>
public class CrossFileSemanticCheckTests
{
    [Fact]
    public void FieldAccessToSerializableClassDefinedInOtherFile_ReportsCrossFileFieldAccess()
    {
        var dataFile = ("ColorPaletteData.cs", @"
using System;

[Serializable]
public class ColorPaletteItem
{
    public int mainColor;
}");

        var behaviourFile = ("Behaviour.cs", @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public ColorPaletteItem palette;

    public void Start()
    {
        int c = palette.mainColor;
    }
}");

        var errors = Program.AnalyzeCodeMultiFile(dataFile, behaviourFile);

        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.CrossFileFieldAccess);
    }

    [Fact]
    public void FieldAccessToSerializableClassInSameFile_NoCrossFileFieldAccessError()
    {
        var behaviourFile = ("Behaviour.cs", @"
using UdonSharp;
using System;

[Serializable]
public class ColorPaletteItem
{
    public int mainColor;
}

public class TestBehaviour : UdonSharpBehaviour
{
    public ColorPaletteItem palette;

    public void Start()
    {
        int c = palette.mainColor;
    }
}");

        var errors = Program.AnalyzeCodeMultiFile(behaviourFile);

        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.CrossFileFieldAccess);
    }

    [Fact]
    public void MethodInvocationOnSerializableClassDefinedInOtherFile_ReportsCrossFileMethodInvocation()
    {
        var dataFile = ("ColorPaletteData.cs", @"
using System;

[Serializable]
public class ColorPaletteItem
{
    public int mainColor;

    public int GetMainColor()
    {
        return mainColor;
    }
}");

        var behaviourFile = ("Behaviour.cs", @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public ColorPaletteItem palette;

    public void Start()
    {
        int c = palette.GetMainColor();
    }
}");

        var errors = Program.AnalyzeCodeMultiFile(dataFile, behaviourFile);

        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.CrossFileMethodInvocation);
    }

    [Fact]
    public void SerializableClassFieldAccessInUdonSharpBehaviourFile_ReportsUdonBehaviourSerializableClassUsage()
    {
        // Same-file case: [System.Serializable] classes aren't supported at all inside an
        // UdonSharpBehaviour file, regardless of whether another file is involved.
        var behaviourFile = ("Behaviour.cs", @"
using UdonSharp;
using System;

[Serializable]
public class ColorPaletteItem
{
    public int mainColor;
}

public class TestBehaviour : UdonSharpBehaviour
{
    public ColorPaletteItem palette;

    public void Start()
    {
        int c = palette.mainColor;
    }
}");

        var errors = Program.AnalyzeCodeMultiFile(behaviourFile);

        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.UdonBehaviourSerializableClassUsage);
    }

    [Fact]
    public void StaticMethodCalledFromUdonSharpAccessingSerializableClassField_ReportsStaticMethodFieldAccess()
    {
        var utilityFile = ("UtilityHelpers.cs", @"
using System;

[Serializable]
public class ColorPaletteItem
{
    public int mainColor;
}

public static class UtilityHelpers
{
    public static int GetMainColor(ColorPaletteItem item)
    {
        return item.mainColor;
    }
}");

        var behaviourFile = ("Behaviour.cs", @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        UtilityHelpers.GetMainColor(null);
    }
}");

        var errors = Program.AnalyzeCodeMultiFile(utilityFile, behaviourFile);

        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.StaticMethodFieldAccess);
    }

    [Fact]
    public void UncalledStaticMethodInOtherFile_NoStaticMethodFieldAccessError()
    {
        // BuildCallGraph should only flag static methods that are actually reachable from an
        // UdonSharp behaviour; an unrelated static helper elsewhere must not be checked.
        var utilityFile = ("UtilityHelpers.cs", @"
using System;

[Serializable]
public class ColorPaletteItem
{
    public int mainColor;
}

public static class UtilityHelpers
{
    public static int GetMainColor(ColorPaletteItem item)
    {
        return item.mainColor;
    }
}");

        var behaviourFile = ("Behaviour.cs", @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
    }
}");

        var errors = Program.AnalyzeCodeMultiFile(utilityFile, behaviourFile);

        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.StaticMethodFieldAccess);
    }

    [Fact]
    public void StaticFieldOnUserDefinedTypeReferencedFromUdonSharp_ReportsUserDefinedTypeStaticFieldAccess()
    {
        var utilityFile = ("RybColorUtility.cs", @"
public class RybColorUtility
{
    public static int SharedValue;
}");

        var behaviourFile = ("Behaviour.cs", @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        int v = RybColorUtility.SharedValue;
    }
}");

        var errors = Program.AnalyzeCodeMultiFile(utilityFile, behaviourFile);

        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.UserDefinedTypeStaticFieldAccess);
    }

    [Fact]
    public void ConstFieldOnUserDefinedTypeReferencedFromUdonSharp_NoError()
    {
        var utilityFile = ("RybColorUtility.cs", @"
public class RybColorUtility
{
    public const int MaxValue = 100;
}");

        var behaviourFile = ("Behaviour.cs", @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        int v = RybColorUtility.MaxValue;
    }
}");

        var errors = Program.AnalyzeCodeMultiFile(utilityFile, behaviourFile);

        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.UserDefinedTypeStaticFieldAccess);
    }

    [Fact]
    public void StaleCompiledCopyOfSourceTypeInScriptAssemblies_DoesNotBreakCrossFileFieldAccessResolution()
    {
        // Library/ScriptAssemblies contains the Unity/VRC SDK, but also the project's own
        // already-compiled script assemblies (e.g. Assembly-CSharp.dll), so globbing it (#28)
        // means a *.dll reference can contain a type with the exact same name as one of the
        // .cs files we're also parsing as source. Verify that doesn't corrupt symbol resolution:
        // C# prefers the source-declared type over the metadata one for a same-named type
        // (CS0436), so CheckCrossFileFieldAccess should still resolve to the source symbol.
        var dataSource = @"
using System;

[Serializable]
public class ColorPaletteItem
{
    public int mainColor;
}";

        var dir = Path.Combine(Path.GetTempPath(), "UdonSharpLinterTests_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var priorTree = CSharpSyntaxTree.ParseText(dataSource, path: "ColorPaletteData.cs");
            var priorCompilation = CSharpCompilation.Create(
                "Assembly-CSharp",
                new[] { priorTree },
                GetMinimalBclReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var dllPath = Path.Combine(dir, "Assembly-CSharp.dll");
            var emitResult = priorCompilation.Emit(dllPath);
            Assert.True(emitResult.Success, string.Join("\n", emitResult.Diagnostics));

            var dataFile = ("ColorPaletteData.cs", dataSource);
            var behaviourFile = ("Behaviour.cs", @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public ColorPaletteItem palette;

    public void Start()
    {
        int c = palette.mainColor;
    }
}");

            var errors = Program.AnalyzeCodeMultiFile(new[] { dataFile, behaviourFile }, dir);

            Assert.Single(errors, e => e.Code == Program.LintErrorCodes.CrossFileFieldAccess);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static System.Collections.Generic.List<MetadataReference> GetMinimalBclReferences()
    {
        var trustedAssembliesPaths = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "";
        return trustedAssembliesPaths
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }
}
