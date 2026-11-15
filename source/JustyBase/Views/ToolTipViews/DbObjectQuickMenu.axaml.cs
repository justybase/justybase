using JustyBase.ViewModels;

namespace JustyBase.Views.ToolTipViews;

public partial class DbObjectQuickMenu : UserControl
{
    public DbObjectQuickMenu()
    {
        InitializeComponent();
    }
    public void ClickHandler(object sender, RoutedEventArgs args)
    {
        if (this.DataContext is DbObjectQuickMenuViewModel menu)
        {
            menu.CloseAction?.Invoke();
        }
    }
}