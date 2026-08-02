using CommunityToolkit.Mvvm.ComponentModel;

namespace JustyBase.Models.Tools;

public sealed partial class VariableModel : ObservableObject
{
    [ObservableProperty]
    public required partial string VariableName { get; set; }

    [ObservableProperty]
    public required partial string VariableComputedValue { get; set; }

    public required Func<string>? ComputeVariableValueFunc { get; set; } = null!;

    public void RefreshValue()
    {
        if (ComputeVariableValueFunc is not null)
        {
            VariableComputedValue = ComputeVariableValueFunc.Invoke();
        }
    }
}
