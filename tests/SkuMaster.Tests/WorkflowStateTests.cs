using SkuMaster.Core;
using Xunit;

namespace SkuMaster.Tests;
public sealed class WorkflowStateTests
{
    private static OperationResult Result() => new(new(["sku", "status"], [new[] { "1", "" }]), [], [], 1);
    [Fact]
    public void EditingInvalidatesResultAndDisablesSaving()
    {
        var state = new WorkflowState();
        state.Begin();
        state.Complete(Result());
        Assert.True(state.CanExport);
        Assert.True(state.UnsavedResult);
        state.Invalidate();
        Assert.False(state.CanExport);
        Assert.Null(state.Result);
    }
    [Fact]
    public void StartingAnotherCheckDropsStaleResultAndCancellationLeavesNone()
    {
        var state = new WorkflowState();
        state.Complete(Result());
        state.Begin();
        Assert.True(state.IsBusy);
        Assert.False(state.CanExport);
        Assert.Null(state.Result);
        state.Invalidate();
        Assert.False(state.IsBusy);
        Assert.False(state.CanExport);
    }
    [Fact]
    public void SavedResultCanBeExportedAgainButIsNoLongerDirty()
    {
        var state = new WorkflowState();
        state.Complete(Result());
        state.MarkSaved();
        Assert.False(state.UnsavedResult);
        Assert.True(state.CanExport);
    }
}
