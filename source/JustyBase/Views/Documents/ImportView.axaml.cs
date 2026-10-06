using Avalonia.Data;
using JustyBase.Helpers;
using JustyBase.ViewModels.Documents;

namespace JustyBase.Views.Documents;
public partial class ImportView : UserControl
{
    public ImportView()
    {
        InitializeComponent();
        this.DataContextChanged += ImportView_DataContextChanged;
    }
    private void ImportView_DataContextChanged(object? sender, System.EventArgs e)
    {
        if (this.DataContext is ImportViewModel vm)
        {
            vm.ActionFromView ??= ActionFromViewBase;
        }
    }

    private void ActionFromViewBase(string[] headers)
    {
        if (this.DataContext is ImportViewModel vm)
        {
            previewDataGrid.Columns.Clear();
            previewDataGrid.ItemsSource = vm.PreviewRows;
            for (var i = 0; i < headers.Length; ++i)
            {
                int index = i;
                var bb = CompiledBindingFactory.OneWayIndexer<string[]>(
                    index,
                    row => row == null || index < 0 || index >= row.Length ? null : row[index]);
                DataGridBoundColumn col = new DataGridTextColumn()
                {
                    Header = headers[index],
                    MaxWidth = 200,
                    Binding = bb,
                    Width = DataGridLength.Auto,
                    IsReadOnly = true,
                };
                previewDataGrid.Columns.Add(col);
            }
        }
    }
}
