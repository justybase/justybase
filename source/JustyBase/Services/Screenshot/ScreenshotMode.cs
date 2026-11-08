namespace JustyBase.Services.Screenshot;

/// <summary>
/// Marks a process that exists to take a picture of the main window instead of serving a person.
/// The flag travels in the environment so the screenshot harness can start the real application and
/// let it build its usual window; the application only skips the splash screen and the sizing guess.
/// </summary>
public static class ScreenshotMode
{
    public const string Variable = "JUSTYBASE_SCREENSHOT";
    public const string WidthVariable = "JUSTYBASE_SCREENSHOT_WIDTH";
    public const string HeightVariable = "JUSTYBASE_SCREENSHOT_HEIGHT";

    public static bool IsRequested
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable));

    /// <summary>The window width the harness asked for, or null.</summary>
    public static double? Width => ReadSize(WidthVariable);

    /// <summary>The window height the harness asked for, or null.</summary>
    public static double? Height => ReadSize(HeightVariable);

    private static double? ReadSize(string variable)
        => double.TryParse(Environment.GetEnvironmentVariable(variable), out double value) && value >= 200
            ? value
            : null;
}
