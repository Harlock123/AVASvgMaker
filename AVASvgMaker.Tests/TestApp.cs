using Avalonia;
using Avalonia.Headless;
using AVASvgMaker;
using AVASvgMaker.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace AVASvgMaker.Tests;

/// <summary>
/// Starts the real application without a display. The tests drive the real window, the real
/// canvas and real pointer events - anything less would be testing a different program.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia();
}
