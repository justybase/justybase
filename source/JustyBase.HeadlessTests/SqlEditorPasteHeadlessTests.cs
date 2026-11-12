using Avalonia.Controls;
using Avalonia.Input.Platform;
using JustyBase.Editor;

namespace JustyBase.HeadlessTests;

public sealed class SqlEditorPasteHeadlessTests : HeadlessSessionTestBase
{
    [Fact]
    public Task PasteAsync_TabularHandlerCanSuppressThePaste() => RunOnUiAsync(async () =>
    {
        var editor = new SqlCodeEditor { Text = "start" };
        var window = new Window { Content = editor, Width = 600, Height = 400 };
        window.Show();

        try
        {
            editor.CaretOffset = editor.Text.Length;
            await window.Clipboard!.SetTextAsync("A\tB\n1\t2\n3\t4");

            string? captured = null;
            editor.PasteRequestHandler = text =>
            {
                captured = text;
                return Task.FromResult(true);
            };

            await editor.PasteAsync();

            Assert.Equal("A\tB\n1\t2\n3\t4", captured);
            Assert.Equal("start", editor.Text);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task PasteAsync_WhenTheHandlerDeclines_InsertsTheText() => RunOnUiAsync(async () =>
    {
        var editor = new SqlCodeEditor { Text = "start" };
        var window = new Window { Content = editor, Width = 600, Height = 400 };
        window.Show();

        try
        {
            editor.CaretOffset = editor.Text.Length;
            await window.Clipboard!.SetTextAsync("A\tB\n1\t2\n3\t4");

            string? captured = null;
            editor.PasteRequestHandler = text =>
            {
                captured = text;
                return Task.FromResult(false);
            };

            await editor.PasteAsync();

            Assert.Equal("A\tB\n1\t2\n3\t4", captured);
            Assert.StartsWith("startA    B", editor.Text);
            Assert.Contains("3    4", editor.Text);
            Assert.DoesNotContain('\t', editor.Text);
        }
        finally
        {
            window.Close();
        }
    });
}

