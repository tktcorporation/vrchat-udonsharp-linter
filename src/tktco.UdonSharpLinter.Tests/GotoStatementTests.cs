using Xunit;
using tktco.UdonSharpLinter;

namespace tktco.UdonSharpLinter.Tests;

public class GotoStatementTests
{
    [Fact]
    public void GotoLabel_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        goto retry;
        retry:
        DoSomething();
    }

    private void DoSomething() { }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.GotoStatement);
    }

    [Fact]
    public void LabeledStatement_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        myLabel:
        DoSomething();
    }

    private void DoSomething() { }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.GotoStatement);
    }

    [Fact]
    public void GotoCase_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        int x = 1;
        switch (x)
        {
            case 1:
                goto case 2;
            case 2:
                break;
        }
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.GotoStatement);
    }

    [Fact]
    public void GotoDefault_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        int x = 1;
        switch (x)
        {
            case 1:
                goto default;
            default:
                break;
        }
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.GotoStatement);
    }

    [Fact]
    public void WhileLoop_NoError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        bool shouldRetry = true;
        while (shouldRetry)
        {
            DoSomething();
            shouldRetry = false;
        }
    }

    private void DoSomething() { }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.GotoStatement);
    }

    [Fact]
    public void BreakAndContinue_NoError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        for (int i = 0; i < 10; i++)
        {
            if (i == 5) continue;
            if (i == 8) break;
        }
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.GotoStatement);
    }

    [Fact]
    public void SwitchWithoutGoto_NoError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        int x = 1;
        switch (x)
        {
            case 1:
                DoSomething();
                break;
            case 2:
                DoSomethingElse();
                break;
            default:
                break;
        }
    }

    private void DoSomething() { }
    private void DoSomethingElse() { }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.GotoStatement);
    }
}
