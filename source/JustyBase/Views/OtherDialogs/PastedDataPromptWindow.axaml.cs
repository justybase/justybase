using JustyBase.Helpers;

namespace JustyBase.Views.OtherDialogs;

public partial class PastedDataPromptWindow : Window
{
    public PastedDataPromptWindow() : this(string.Empty)
    {
    }

    public PastedDataPromptWindow(string message)
    {
        InitializeComponent();
        messageText.Text = message;
        Opened += (_, _) => btnImport.Focus();
        btnImport.Click += (_, _) => Close(TabularPasteChoice.ImportData);
        btnPasteAsText.Click += (_, _) => Close(TabularPasteChoice.PasteAsText);
        btnCancel.Click += (_, _) => Close(TabularPasteChoice.Cancel);
    }
}
