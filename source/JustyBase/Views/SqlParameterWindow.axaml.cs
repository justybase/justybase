using JustyBase.ViewModels;

namespace JustyBase.Views;

public partial class SqlParameterWindow : Window
{
    public SqlParameterWindow()
    {
        InitializeComponent();
        this.DataContextChanged += SqlParameterWindow_DataContextChanged;
        this.Activated += SqlParameterWindow_Activated;
    }

    private void SqlParameterWindow_DataContextChanged(object? sender, EventArgs e)
    {
        (this.DataContext as SqlParameterViewModel).CloseAction = () => this.Close();
    }

    private void SqlParameterWindow_Activated(object? sender, EventArgs e)
    {
        dg.Focus();
        dg.SelectedIndex = 0;
        dg.CurrentColumn = dg.Columns[1];
        dg.BeginEdit();
    }

    private void TextBox_Initialized(object? sender, System.EventArgs e)
    {
        TextBox tb = (TextBox)sender;
        tb.Focus();
        tb.SelectAll();
    }
}