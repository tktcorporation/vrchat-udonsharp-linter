using Xunit;
using tktco.UdonSharpLinter;

namespace tktco.UdonSharpLinter.Tests;

public class UserDefinedTypeStaticFieldTests
{
    #region Static Field Access on User-Defined Types

    [Fact]
    public void UserDefinedType_StaticFieldAccess_ReportsError()
    {
        var code = @"
using UdonSharp;

public class RybColorUtility
{
    public static int Counter = 0;
}

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        var c = RybColorUtility.Counter;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.UserDefinedTypeStaticFieldAccess);
    }

    [Fact]
    public void UserDefinedType_StaticFieldWrite_ReportsError()
    {
        var code = @"
using UdonSharp;

public class MyHelper
{
    public static string SharedData;
}

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        MyHelper.SharedData = ""test"";
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.UserDefinedTypeStaticFieldAccess);
    }

    [Fact]
    public void UserDefinedType_StaticFieldIncrement_ReportsError()
    {
        var code = @"
using UdonSharp;

public class Counter
{
    public static int Value = 0;
}

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        Counter.Value++;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.UserDefinedTypeStaticFieldAccess);
    }

    #endregion

    #region Allowed Cases - const

    [Fact]
    public void UserDefinedType_ConstField_NoError()
    {
        var code = @"
using UdonSharp;

public class MyConstants
{
    public const int MAX_VALUE = 100;
    public const string PREFIX = ""Player"";
}

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        var max = MyConstants.MAX_VALUE;
        var prefix = MyConstants.PREFIX;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.UserDefinedTypeStaticFieldAccess);
    }

    #endregion

    #region Allowed Cases - static readonly

    [Fact]
    public void UserDefinedType_StaticReadonlyField_NoError()
    {
        var code = @"
using UdonSharp;

public class MyConstants
{
    public static readonly int MaxPlayers = 10;
}

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        var max = MyConstants.MaxPlayers;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.UserDefinedTypeStaticFieldAccess);
    }

    #endregion

    #region Allowed Cases - Unity/System Types

    [Fact]
    public void UnityType_StaticField_NoError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        var pi = UnityEngine.Mathf.PI;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.UserDefinedTypeStaticFieldAccess);
    }

    [Fact]
    public void SystemType_StaticField_NoError()
    {
        var code = @"
using UdonSharp;
using System;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        var max = Int32.MaxValue;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.UserDefinedTypeStaticFieldAccess);
    }

    #endregion

    #region UdonSharpBehaviour Static Fields (covered by existing check)

    [Fact]
    public void UdonSharpBehaviour_StaticFieldDefinition_ReportsExistingError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public static int counter = 0;
}";
        var errors = Program.AnalyzeCode(code);
        // This should be caught by the existing CheckStaticFields check (UDON011)
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.StaticField);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void NonUdonSharpBehaviour_StaticFieldAccess_NoError()
    {
        // If the code is not in an UdonSharpBehaviour, no error should be reported
        var code = @"
public class RybColorUtility
{
    public static int Counter = 0;
}

public class RegularClass
{
    public void Start()
    {
        var c = RybColorUtility.Counter;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.UserDefinedTypeStaticFieldAccess);
    }

    [Fact]
    public void UserDefinedType_MultipleStaticFieldAccesses_ReportsMultipleErrors()
    {
        var code = @"
using UdonSharp;

public class Config
{
    public static int Value1 = 0;
    public static int Value2 = 0;
}

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        var v1 = Config.Value1;
        var v2 = Config.Value2;
    }
}";
        var errors = Program.AnalyzeCode(code);
        var staticFieldErrors = errors.Where(e => e.Code == Program.LintErrorCodes.UserDefinedTypeStaticFieldAccess).ToList();
        Assert.Equal(2, staticFieldErrors.Count);
    }

    #endregion
}
