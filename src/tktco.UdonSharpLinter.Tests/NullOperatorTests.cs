using Xunit;
using tktco.UdonSharpLinter;

namespace tktco.UdonSharpLinter.Tests;

public class NullOperatorTests
{
    #region Null Conditional Operator (?.)

    [Fact]
    public void NullConditional_MethodCall_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    private string player;

    public void Start()
    {
        var name = player?.ToString();
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.NullConditionalOperator);
    }

    [Fact]
    public void NullConditional_PropertyAccess_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    private string player;

    public void Start()
    {
        var length = player?.Length;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.NullConditionalOperator);
    }

    [Fact]
    public void NullConditional_ElementAccess_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    private int[] numbers;

    public void Start()
    {
        var first = numbers?[0];
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.NullConditionalOperator);
    }

    [Fact]
    public void ExplicitNullCheck_NoError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    private string player;

    public void Start()
    {
        if (player != null)
        {
            var name = player.ToString();
        }
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.NullConditionalOperator);
    }

    #endregion

    #region Null Coalescing Operator (??)

    [Fact]
    public void NullCoalescing_BasicUsage_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    private string playerName;

    public void Start()
    {
        var name = playerName ?? ""Guest"";
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.NullCoalescingOperator);
    }

    [Fact]
    public void NullCoalescing_ChainedUsage_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    private string a;
    private string b;

    public void Start()
    {
        var result = a ?? b ?? ""default"";
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.NullCoalescingOperator);
    }

    [Fact]
    public void NullCoalescingAssignment_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    private string playerName;

    public void Start()
    {
        playerName ??= ""Guest"";
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.NullCoalescingOperator);
    }

    [Fact]
    public void TernaryWithNullCheck_NoError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    private string playerName;

    public void Start()
    {
        var name = playerName != null ? playerName : ""Guest"";
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.NullCoalescingOperator);
    }

    #endregion
}
