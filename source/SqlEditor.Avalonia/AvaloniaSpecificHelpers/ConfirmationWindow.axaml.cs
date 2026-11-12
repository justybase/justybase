namespace JustyBase.Views
{
    public partial class ConfirmationWindow : Window
    {
        public ConfirmationWindow() : this("Do you want to proceed?", "Confirmation")
        {
        }

        public ConfirmationWindow(
            string content = "Do you want to proceed?",
            string title = "Confirmation",
            string confirmText = "Yes",
            string cancelText = "No")
        {
            InitializeComponent();
            Title = title;
            titleLabel.Text = title;
            contentTextBlock.Text = content;
            btnYes.Content = confirmText;
            btnNo.Content = cancelText;
            btnYes.Click += (s, e) => Close(true);
            btnNo.Click += (s, e) => Close(false);
        }
    }
}