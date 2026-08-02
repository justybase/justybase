namespace JustyBase.Views.OtherDialogs;

public partial class AskForFileName : Window
{
    public AskForFileName() : this(false)
    {
    }
    public AskForFileName(bool gotoLine = false, bool isRename = false)
    {
        InitializeComponent();
        fileNameTb.KeyDown += Tb_KeyDown;
        btOk.Click += BtOk_Click;
        btClose.Click += BtClose_Click;
        if (gotoLine)
        {
            fileNameTb.PlaceholderText = "line number";
            tbExt.Text = "";
        }
        if (isRename)
        {
            Title = "Rename symbol";
            fileNameTb.PlaceholderText = "new name";
            tbExt.Text = "";
        }
        this.Activated += AskForFileName_Activated;
    }

    private void AskForFileName_Activated(object? sender, System.EventArgs e)
    {
        fileNameTb.Focus();
        fileNameTb.IsEnabled = true;
        fileNameTb.SelectionStart = 0;
    }

    private void BtClose_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ReturnedName = null;
        this.Close();
    }

    private void Tb_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            ReturnedName = fileNameTb.Text;
            this.Close();
        }
        if (e.Key == Key.Escape)
        {
            ReturnedName = null;
            this.Close();
        }
    }
    private void BtOk_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ReturnedName = fileNameTb.Text;
        this.Close();
    }

    public string? ReturnedName { get; set; }
}
