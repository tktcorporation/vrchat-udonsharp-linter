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
    public void BareNoneSyncModeWithUdonSyncedField_ReportsError()
    {
        var code = @"
using UdonSharp;
using VRC.SDKBase;
using static VRC.SDKBase.BehaviourSyncMode;

[UdonBehaviourSyncMode(None)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced]
    public int value;
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.SyncModeConflict);
    }

    [Fact]
    public void GroupedNoVariableSyncFields_ReportsErrorPerVariable()
    {
        var code = @"
using UdonSharp;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced]
    public int a, b, c;
}";
        var errors = Program.AnalyzeCode(code);
        var conflictErrors = errors.FindAll(e => e.Code == Program.LintErrorCodes.SyncModeConflict);
        Assert.Equal(3, conflictErrors.Count);
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
    public void ManualSyncCallingRequestSerializationOnOtherObject_StillReportsWarning()
    {
        var code = @"
using UdonSharp;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced]
    public int value;

    public TestBehaviour other;

    public void SetValue(int newValue)
    {
        value = newValue;
        other.RequestSerialization();
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.ManualSyncMissingRequestSerialization);
    }

    [Fact]
    public void ManualSyncCallingRequestSerializationViaBase_NoWarning()
    {
        var code = @"
using UdonSharp;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced]
    public int value;

    public override void OnDeserialization()
    {
        base.RequestSerialization();
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
    public void SyncedInt32AliasArray_ReportsWarning()
    {
        var code = @"
using UdonSharp;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced]
    public System.Int32[] values;
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

    [Fact]
    public void DecoyAttributeNamedLikeUdonSynced_DoesNotTriggerSyncModeConflict()
    {
        // [UdonSyncedMetadata] merely *contains* "UdonSynced" as a substring; it must not be
        // mistaken for the real [UdonSynced] attribute (regression test for #27).
        var code = @"
using UdonSharp;
using VRC.SDKBase;
using System;

public class UdonSyncedMetadataAttribute : Attribute { }

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSyncedMetadata]
    public int value;
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.SyncModeConflict);
    }

    [Fact]
    public void DecoyAttributeNamedLikeUdonBehaviourSyncMode_IsNotTreatedAsSyncModeDeclaration()
    {
        // A class attribute that merely *contains* "UdonBehaviourSyncMode" as a substring (and that
        // happens to carry a NoVariableSync-looking argument) must not be mistaken for the real
        // [UdonBehaviourSyncMode(...)] attribute (regression test for #27).
        var code = @"
using UdonSharp;
using VRC.SDKBase;
using System;

public class MyUdonBehaviourSyncModeMarkerAttribute : Attribute
{
    public MyUdonBehaviourSyncModeMarkerAttribute(object mode) { }
}

[MyUdonBehaviourSyncModeMarker(BehaviourSyncMode.NoVariableSync)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced]
    public int value;
}";
        var errors = Program.AnalyzeCode(code);
        // No real [UdonBehaviourSyncMode(...)] attribute is present, so GetUdonBehaviourSyncModeArgument
        // should return null and neither sync-mode check should fire.
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.SyncModeConflict);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.ManualSyncMissingRequestSerialization);
    }

    [Fact]
    public void SyncModeViaConstantIndirection_NoVariableSync_ReportsSyncModeConflict()
    {
        // The sync mode is referenced through a const field rather than a direct enum member
        // access, so the text-based rightmost-segment check ("NoSyncConstant" is neither "None"
        // nor "NoVariableSync") can't catch it on its own — this needs semantic constant-value
        // resolution against the real (here, stubbed) VRC.SDKBase.BehaviourSyncMode enum
        // (regression test for #25).
        var code = @"
using UdonSharp;

namespace VRC.SDKBase
{
    public enum BehaviourSyncMode
    {
        None,
        Manual,
        Continuous
    }
}

public static class SyncModeConstants
{
    public const VRC.SDKBase.BehaviourSyncMode NoSyncConstant = VRC.SDKBase.BehaviourSyncMode.None;
}

[UdonBehaviourSyncMode(SyncModeConstants.NoSyncConstant)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced]
    public int value;
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.SyncModeConflict);
    }

    [Fact]
    public void SyncModeViaConstantIndirection_ManualMode_NoConflictError()
    {
        var code = @"
using UdonSharp;

namespace VRC.SDKBase
{
    public enum BehaviourSyncMode
    {
        None,
        Manual,
        Continuous
    }
}

public static class SyncModeConstants
{
    public const VRC.SDKBase.BehaviourSyncMode ActiveSyncMode = VRC.SDKBase.BehaviourSyncMode.Manual;
}

[UdonBehaviourSyncMode(SyncModeConstants.ActiveSyncMode)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced]
    public int value;

    public void SetValue(int v)
    {
        value = v;
        RequestSerialization();
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.SyncModeConflict);
    }

    [Fact]
    public void SyncModeViaConstantIndirection_ManualModeMissingRequestSerialization_ReportsWarning()
    {
        // Same constant-indirection shape as above, but verifying IsManualSyncMode's semantic
        // fallback (not just IsNoSyncMode's) picks up the Manual sync mode correctly.
        var code = @"
using UdonSharp;

namespace VRC.SDKBase
{
    public enum BehaviourSyncMode
    {
        None,
        Manual,
        Continuous
    }
}

public static class SyncModeConstants
{
    public const VRC.SDKBase.BehaviourSyncMode ActiveSyncMode = VRC.SDKBase.BehaviourSyncMode.Manual;
}

[UdonBehaviourSyncMode(SyncModeConstants.ActiveSyncMode)]
public class TestBehaviour : UdonSharpBehaviour
{
    [UdonSynced]
    public int value;

    public void SetValue(int v)
    {
        value = v;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.ManualSyncMissingRequestSerialization);
    }
}
