using Xunit;
using tktco.UdonSharpLinter;

namespace tktco.UdonSharpLinter.Tests;

public class AttributeMatchingTests
{
    [Fact]
    public void NetworkCallable_DecoyAttributeContainingName_DoesNotTriggerChecks()
    {
        // [NetworkCallableMetadata] merely *contains* "NetworkCallable"; the
        // old substring match treated it as the real attribute and flagged the
        // non-void return type.
        var code = @"
using UdonSharp;

public class NetworkCallableMetadataAttribute : System.Attribute { }

public class TestBehaviour : UdonSharpBehaviour
{
    [NetworkCallableMetadata]
    public int GetValue()
    {
        return 1;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.NetworkCallable);
    }

    [Fact]
    public void NetworkCallable_ExactName_StillTriggersChecks()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    [NetworkCallable]
    public int GetValue()
    {
        return 1;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.NetworkCallable);
    }

    [Fact]
    public void NetworkCallable_ExplicitAttributeSuffix_StillTriggersChecks()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    [NetworkCallableAttribute]
    public int GetValue()
    {
        return 1;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.NetworkCallable);
    }

    [Fact]
    public void NetworkCallable_NamespaceQualified_StillTriggersChecks()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    [VRC.SDK3.UdonNetworkCalling.NetworkCallable]
    public int GetValue()
    {
        return 1;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.NetworkCallable);
    }

    [Fact]
    public void NetworkCallable_AliasQualified_StillTriggersChecks()
    {
        // global:: produces an AliasQualifiedNameSyntax, which a last-dot
        // string split does not reduce to a simple name.
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    [global::NetworkCallable]
    public int GetValue()
    {
        return 1;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.NetworkCallable);
    }
}
