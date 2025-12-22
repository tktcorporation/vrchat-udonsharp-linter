using Xunit;
using tktco.UdonSharpLinter;

namespace tktco.UdonSharpLinter.Tests;

public class AsyncAwaitTests
{
    [Fact]
    public void AsyncMethod_ReportsError()
    {
        var code = @"
using UdonSharp;
using System.Threading.Tasks;

public class TestBehaviour : UdonSharpBehaviour
{
    public async void Start()
    {
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.AsyncAwait);
    }

    [Fact]
    public void AsyncMethodWithTask_ReportsError()
    {
        var code = @"
using UdonSharp;
using System.Threading.Tasks;

public class TestBehaviour : UdonSharpBehaviour
{
    public async Task DoSomethingAsync()
    {
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.AsyncAwait);
    }

    [Fact]
    public void AsyncMethodWithTaskOfT_ReportsError()
    {
        var code = @"
using UdonSharp;
using System.Threading.Tasks;

public class TestBehaviour : UdonSharpBehaviour
{
    public async Task<int> GetValueAsync()
    {
        return 42;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.AsyncAwait);
    }

    [Fact]
    public void AwaitExpression_ReportsError()
    {
        var code = @"
using UdonSharp;
using System.Threading.Tasks;

public class TestBehaviour : UdonSharpBehaviour
{
    public async void Start()
    {
        await Task.Delay(1000);
    }
}";
        var errors = Program.AnalyzeCode(code);
        // Should report both async method and await expression
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.AsyncAwait);
    }

    [Fact]
    public void AsyncLocalFunction_ReportsError()
    {
        var code = @"
using UdonSharp;
using System.Threading.Tasks;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        async void LocalAsync()
        {
        }
    }
}";
        var errors = Program.AnalyzeCode(code);
        // Should report async local function (and also local function error)
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.AsyncAwait);
    }

    [Fact]
    public void RegularMethod_NoAsyncError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
    }

    public void Update()
    {
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.AsyncAwait);
    }

    [Fact]
    public void SendCustomEventDelayedSeconds_IsValidAlternative()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        SendCustomEventDelayedSeconds(""DelayedAction"", 1.0f);
    }

    public void DelayedAction()
    {
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.AsyncAwait);
    }
}
