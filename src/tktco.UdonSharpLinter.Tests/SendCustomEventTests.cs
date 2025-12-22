using Xunit;
using tktco.UdonSharpLinter;

namespace tktco.UdonSharpLinter.Tests;

public class SendCustomEventTests
{
    [Fact]
    public void SendCustomEvent_WithExistingMethod_NoError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        SendCustomEvent(""OnDamage"");
    }

    public void OnDamage()
    {
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.SendCustomEventMethodNotFound);
    }

    [Fact]
    public void SendCustomEvent_WithMissingMethod_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        SendCustomEvent(""OnDamege"");
    }

    public void OnDamage()
    {
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.SendCustomEventMethodNotFound);
    }

    [Fact]
    public void SendCustomEvent_WithThisPrefix_NoError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        this.SendCustomEvent(""OnDamage"");
    }

    public void OnDamage()
    {
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.SendCustomEventMethodNotFound);
    }

    [Fact]
    public void SendCustomEventDelayedSeconds_WithMissingMethod_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        SendCustomEventDelayedSeconds(""NonExistentMethod"", 1.0f);
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.SendCustomEventMethodNotFound);
    }

    [Fact]
    public void SendCustomEventDelayedFrames_WithMissingMethod_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        SendCustomEventDelayedFrames(""NonExistentMethod"", 10);
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.SendCustomEventMethodNotFound);
    }

    [Fact]
    public void SendCustomNetworkEvent_WithMissingMethod_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        SendCustomNetworkEvent(NetworkEventTarget.All, ""NonExistentMethod"");
    }
}

public enum NetworkEventTarget { All, Owner }
";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.SendCustomEventMethodNotFound);
    }

    [Fact]
    public void SendCustomEvent_WithVariableMethodName_NoError()
    {
        // 変数でメソッド名を指定する場合は静的解析できないのでエラーにしない
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        string methodName = ""SomeMethod"";
        SendCustomEvent(methodName);
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.SendCustomEventMethodNotFound);
    }
}
