using Xunit;
using tktco.UdonSharpLinter;

namespace tktco.UdonSharpLinter.Tests;

public class SynchronizationConstraintTests
{
    [Fact]
    public void NoVariableSyncWithUdonSyncedField_ReportsError()
    {
        var code = @"
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced]
    public int value;
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.SyncModeConflict);
    }

    [Fact]
    public void ManualSyncWithUdonSyncedField_NoConflictError()
    {
        var code = @"
using UdonSharp;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced]
    public int value;

    public void SetValue(int newValue)
    {
        value = newValue;
        RequestSerialization();
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.SyncModeConflict);
    }

    [Fact]
    public void ManualSyncMissingRequestSerialization_ReportsWarning()
    {
        var code = @"
using UdonSharp;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced]
    public int value;

    public void SetValue(int newValue)
    {
        value = newValue;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.ManualSyncMissingRequestSerialization);
    }

    [Fact]
    public void ManualSyncWithRequestSerialization_NoWarning()
    {
        var code = @"
using UdonSharp;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced]
    public int value;

    public void SetValue(int newValue)
    {
        value = newValue;
        RequestSerialization();
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.ManualSyncMissingRequestSerialization);
    }

    [Fact]
    public void ExcessiveSyncedFields_ReportsWarning()
    {
        var code = @"
using UdonSharp;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced] public int value1;
    [UdonSynced] public int value2;
    [UdonSynced] public int value3;
    [UdonSynced] public int value4;
    [UdonSynced] public int value5;
    [UdonSynced] public int value6;
    [UdonSynced] public int value7;
    [UdonSynced] public int value8;
    [UdonSynced] public int value9;
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.ExcessiveSyncedVariables);
    }

    [Fact]
    public void ExcessiveSyncedFieldsGroupedInSingleDeclaration_ReportsWarning()
    {
        var code = @"
using UdonSharp;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced] public int value1, value2, value3, value4, value5, value6, value7, value8, value9;
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.ExcessiveSyncedVariables);
    }

    [Fact]
    public void FewSyncedFields_NoWarning()
    {
        var code = @"
using UdonSharp;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced] public int value1;
    [UdonSynced] public int value2;
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.ExcessiveSyncedVariables);
    }

    [Fact]
    public void SyncedIntArray_ReportsWarning()
    {
        var code = @"
using UdonSharp;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced]
    public int[] values;
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.LargeArraySynced);
    }

    [Fact]
    public void SyncedByteArray_NoWarning()
    {
        var code = @"
using UdonSharp;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced]
    public byte[] values;
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.LargeArraySynced);
    }
}
