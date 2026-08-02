using Avalonia.Controls;
using JustyBase.ViewModels;

namespace JustyBase.Views.OtherDialogs;

public partial class NetezzaMaintenanceDialog : Window
{
    public NetezzaMaintenanceDialog()
    {
        InitializeComponent();
    }

    public NetezzaMaintenanceDialog(NetezzaMaintenanceDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseAction = () => Close(viewModel.Confirmed);
    }
}
