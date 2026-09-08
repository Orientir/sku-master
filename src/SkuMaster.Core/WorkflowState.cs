namespace SkuMaster.Core;

public sealed class WorkflowState
{
    public OperationResult? Result { get; private set; }
    public bool IsBusy { get; private set; }
    public bool UnsavedResult { get; private set; }
    public bool CanExport => Result != null && !IsBusy;
    public void Begin() { Invalidate(); IsBusy = true; }
    public void Complete(OperationResult result) { Result = result; IsBusy = false; UnsavedResult = true; }
    public void Invalidate() { Result = null; IsBusy = false; UnsavedResult = false; }
    public void MarkSaved() { UnsavedResult = false; }
}
