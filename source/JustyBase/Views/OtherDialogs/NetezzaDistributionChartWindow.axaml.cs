using JustyBase.ViewModels;

namespace JustyBase.Views.OtherDialogs;

public partial class NetezzaDistributionChartWindow : Window
{
    public NetezzaDistributionChartWindow()
    {
        InitializeComponent();
    }

    public NetezzaDistributionChartWindow(NetezzaDistributionChartViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
