using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using JustyBase.Services;

namespace JustyBase.ViewModels;

public sealed partial class NetezzaDistributionChartViewModel : ObservableObject
{
    public NetezzaDistributionChartViewModel(NetezzaSkewResult result)
    {
        Result = result;
        Title = $"Distribution — {result.QualifiedTable}";
        Summary = result.Summary;
        foreach (var slice in result.Slices)
        {
            var pct = result.MaxRows == 0 ? 0 : (double)slice.RowCount / result.MaxRows;
            Slices.Add(new SkewSliceBar(slice.DataSliceId, slice.RowCount, pct));
        }
    }

    public NetezzaSkewResult Result { get; }
    public string Title { get; }
    public string Summary { get; }
    public ObservableCollection<SkewSliceBar> Slices { get; } = [];
}

public sealed record SkewSliceBar(long DataSliceId, long RowCount, double RelativeWidth);
