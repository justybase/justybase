using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace JustyBase.Views;

public partial class ThirdPartyNoticesWindow : Window
{
    private const string ResourceName = "JustyBase.THIRD_PARTY_NOTICES.md";

    public ThirdPartyNoticesWindow()
    {
        InitializeComponent();
        NoticesText.Text = LoadNotices();
    }

    private static string LoadNotices()
    {
        using var stream = typeof(ThirdPartyNoticesWindow).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return "Third-party notices are not embedded in this build.";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
