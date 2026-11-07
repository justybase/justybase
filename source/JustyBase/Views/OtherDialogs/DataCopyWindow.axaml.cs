using JustyBase.ViewModels;

namespace JustyBase.Views.OtherDialogs;

public partial class DataCopyWindow : Window
{
    public DataCopyWindow()
    {
        InitializeComponent();
    }

    public DataCopyWindow(DataCopyViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseAction = Close;
    }
}
