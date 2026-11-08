using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Compares a rendered window with a committed PNG. Differences are allowed only below a small
/// per-channel threshold and only on a small fraction of pixels, so ClearType and driver-level
/// rounding do not flap the suite while real layout or colour regressions still fail.
///
/// Refresh the baselines with:
///   $env:JUSTYBASE_UPDATE_SNAPSHOTS=1
///   dotnet test source\JustyBase.HeadlessTests\JustyBase.HeadlessTests.csproj -c Debug
/// </summary>
internal static class VisualRegression
{
    public const string UpdateVariable = "JUSTYBASE_UPDATE_SNAPSHOTS";

    private const int PerChannelTolerance = 12;
    private const double MaxDifferentPixelRatio = 0.02;

    public static void AssertMatches(
        string name,
        Window window,
        [CallerFilePath] string sourceFile = "")
    {
        WriteableBitmap? frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Assert.Fail($"'{name}' rendered no frame.");
            return;
        }

        using (frame)
        {
            string sourceDirectory = Path.GetDirectoryName(sourceFile)!;
            string baselineDirectory = Path.Combine(sourceDirectory, "VisualBaselines");
            Directory.CreateDirectory(baselineDirectory);
            string baselinePath = Path.Combine(baselineDirectory, name + ".png");

            if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
            {
                frame.Save(baselinePath, PngBitmapEncoderOptions.Default);
                return;
            }

            if (!File.Exists(baselinePath))
            {
                SaveArtifacts(name, ReadPixels(frame), ReadPixels(frame), frame.PixelSize, sourceDirectory);
                Assert.Fail(
                    $"No visual baseline for '{name}'. Create it with {UpdateVariable}=1, then commit {baselinePath}.");
                return;
            }

            using var baseline = new Bitmap(baselinePath);
            if (baseline.PixelSize != frame.PixelSize)
            {
                SaveArtifacts(name, ReadPixels(frame), ReadPixels(frame), frame.PixelSize, sourceDirectory);
                Assert.Fail(
                    $"Baseline for '{name}' is {baseline.PixelSize.Width}x{baseline.PixelSize.Height}, " +
                    $"but the window rendered {frame.PixelSize.Width}x{frame.PixelSize.Height}.");
                return;
            }

            byte[] actual = ReadPixels(frame);
            byte[] expected = ReadPixels(baseline);
            PixelSize size = frame.PixelSize;
            int stride = size.Width * 4;

            int differentPixels = 0;
            byte[] diff = new byte[actual.Length];
            for (int y = 0; y < size.Height; y++)
            {
                for (int x = 0; x < size.Width; x++)
                {
                    int offset = y * stride + x * 4;
                    int delta = Math.Max(
                        Math.Max(
                            Math.Abs(actual[offset] - expected[offset]),
                            Math.Abs(actual[offset + 1] - expected[offset + 1])),
                        Math.Abs(actual[offset + 2] - expected[offset + 2]));

                    if (delta > PerChannelTolerance)
                    {
                        differentPixels++;
                        diff[offset] = 255;
                        diff[offset + 1] = 0;
                        diff[offset + 2] = 255;
                    }
                    else
                    {
                        diff[offset] = (byte)(expected[offset] / 2);
                        diff[offset + 1] = (byte)(expected[offset + 1] / 2);
                        diff[offset + 2] = (byte)(expected[offset + 2] / 2);
                    }

                    diff[offset + 3] = 255;
                }
            }

            int totalPixels = size.Width * size.Height;
            if (differentPixels > totalPixels * MaxDifferentPixelRatio)
            {
                string artifactDirectory = SaveArtifacts(name, actual, diff, size, sourceDirectory);
                Assert.Fail(
                    $"Visual baseline mismatch for '{name}': {differentPixels:N0} of {totalPixels:N0} pixels differ " +
                    $"(allowed {MaxDifferentPixelRatio:P1}). Baseline: {baselinePath}. " +
                    $"Actual/diff: {artifactDirectory}. Refresh with {UpdateVariable}=1.");
            }
        }
    }

    private static byte[] ReadPixels(Bitmap bitmap)
    {
        PixelSize size = bitmap.PixelSize;
        int stride = size.Width * 4;
        byte[] buffer = new byte[stride * size.Height];
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(
                new PixelRect(0, 0, size.Width, size.Height),
                handle.AddrOfPinnedObject(),
                buffer.Length,
                stride);
        }
        finally
        {
            handle.Free();
        }

        if ((bitmap.Format ?? PixelFormat.Bgra8888) == PixelFormat.Rgba8888)
        {
            for (int i = 0; i + 3 < buffer.Length; i += 4)
            {
                (buffer[i], buffer[i + 2]) = (buffer[i + 2], buffer[i]);
            }
        }

        return buffer;
    }

    private static string SaveArtifacts(
        string name,
        byte[] actual,
        byte[] image,
        PixelSize size,
        string sourceDirectory)
    {
        string directory = Path.GetFullPath(
            Path.Combine(sourceDirectory, "..", "..", "TestResults", "visual"));
        Directory.CreateDirectory(directory);
        SavePixels(Path.Combine(directory, name + ".actual.png"), actual, size);
        SavePixels(Path.Combine(directory, name + ".diff.png"), image, size);
        return directory;
    }

    private static void SavePixels(string path, byte[] pixels, PixelSize size)
    {
        using var bitmap = new WriteableBitmap(
            size,
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using (ILockedFramebuffer frame = bitmap.Lock())
        {
            int stride = size.Width * 4;
            for (int y = 0; y < size.Height; y++)
            {
                Marshal.Copy(
                    pixels,
                    y * stride,
                    IntPtr.Add(frame.Address, y * frame.RowBytes),
                    stride);
            }
        }

        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }
}
