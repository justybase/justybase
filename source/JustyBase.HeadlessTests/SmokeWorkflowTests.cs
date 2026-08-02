using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using JustyBase.Helpers.Shared;
using JustyBase.Services.Documents;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Lightweight Avalonia headless smokes for open-doc / run / export paths (N10 lite).
/// Uses HeadlessUnitTestSession (xUnit 2 compatible) instead of Avalonia.Headless.XUnit / xUnit v3.
/// </summary>
public sealed class SmokeWorkflowTests : HeadlessSessionTestBase
{
    private static readonly string[] ExportFormats = [".csv", ".xlsb", ".parquet"];

    [Fact]
    public Task Window_CaptureRenderedFrame_Succeeds() => RunOnUi(() =>
    {
        var window = new Window
        {
            Width = 480,
            Height = 320,
            Title = "JustyBase Headless Smoke"
        };
        window.Show();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    });

    [Fact]
    public Task OpenDocument_TextBoxAcceptsSqlContent() => RunOnUi(() =>
    {
        var editor = new TextBox
        {
            AcceptsReturn = true,
            Width = 400,
            Height = 200,
            PlaceholderText = "SQL document"
        };
        var window = new Window
        {
            Width = 480,
            Height = 320,
            Content = editor
        };
        window.Show();

        editor.Focus();
        window.KeyTextInput("SELECT 1;");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("SELECT 1;", editor.Text);
    });

    [Fact]
    public Task RunMocked_ButtonUpdatesStatus() => RunOnUi(() =>
    {
        var status = new TextBlock { Text = "Idle" };
        var runButton = new Button { Content = "Run" };
        var lastValidationOk = false;

        runButton.Click += (_, _) =>
        {
            var validation = new SqlRunStartValidationResult(SqlRunStartValidationStatus.Ready, null);
            lastValidationOk = validation.CanRun;
            status.Text = validation.CanRun ? "Running (mocked)" : validation.MessageForUser ?? "Blocked";
        };

        var window = new Window
        {
            Width = 480,
            Height = 320,
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                Children = { runButton, status }
            }
        };
        window.Show();

        runButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.True(lastValidationOk);
        Assert.Equal("Running (mocked)", status.Text);
    });

    [Fact]
    public Task Export_ComboSelection_ResolvesCsvPathSpec() => RunOnUi(() =>
    {
        var formats = new ComboBox
        {
            ItemsSource = ExportFormats,
            Width = 200
        };
        var summary = new TextBlock();

        void UpdateSummary()
        {
            var option = formats.SelectedItem as string;
            var spec = SqlExportPathHelper.ResolveExportSpec(option ?? ".csv");
            summary.Text = $"{spec.FileTypeLabel}|{spec.Pattern}|{spec.DefaultExtension}";
        }

        formats.SelectionChanged += (_, _) => UpdateSummary();

        var window = new Window
        {
            Width = 480,
            Height = 320,
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                Children = { formats, summary }
            }
        };
        window.Show();

        formats.SelectedItem = ".csv";
        Dispatcher.UIThread.RunJobs();
        UpdateSummary();

        Assert.Equal("csv files|*.csv|csv", summary.Text);

        formats.SelectedItem = ".parquet";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("parquet files|*.parquet|parquet", summary.Text);
    });

    [Fact]
    public Task OpenDocument_TitleReflectsFilePath() => RunOnUi(() =>
    {
        const string path = @"C:\work\query.sql";
        var title = new TextBlock { Text = "Untitled" };
        var openButton = new Button { Content = "Open" };
        openButton.Click += (_, _) =>
        {
            title.Text = Path.GetFileName(path);
        };

        var window = new Window
        {
            Width = 480,
            Height = 320,
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                Children = { openButton, title }
            }
        };
        window.Show();

        openButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("query.sql", title.Text);
    });
}
