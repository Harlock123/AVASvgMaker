using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;

namespace AVASvgMaker.Engine;

/// <summary>
/// Notices the compositor's display scale changing.
///
/// This exists because the app cannot follow such a change on its own. Avalonia has no Wayland
/// backend, so under Hyprland it runs through XWayland and takes its scale from the
/// AVALONIA_GLOBAL_SCALE_FACTOR environment variable - read once, at startup. Nothing reaches
/// a running process when the monitor is rescaled, so the honest thing to do is notice and say
/// so, rather than half-rescale the drawing and leave the menus and panels behind.
///
/// The scale is read straight off Hyprland's IPC socket rather than by running hyprctl, so
/// polling costs a socket round trip instead of a process.
/// </summary>
public sealed class DisplayScaleWatcher : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly double _startingScale;

    private double _reported;

    /// <summary>Raised when the scale differs from the one the app started with.</summary>
    public event Action<double>? ScaleChanged;

    public static bool IsAvailable => SocketPath is not null;

    private static string? SocketPath
    {
        get
        {
            var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            var instance = Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE");

            if (string.IsNullOrEmpty(runtime) || string.IsNullOrEmpty(instance))
                return null;

            var path = Path.Combine(runtime, "hypr", instance, ".socket.sock");
            return File.Exists(path) ? path : null;
        }
    }

    public DisplayScaleWatcher(TimeSpan interval)
    {
        _startingScale = ReadScale() ?? 1;
        _reported = _startingScale;

        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    private void Check()
    {
        if (ReadScale() is not { } scale)
            return;

        // Report each new value once, rather than every tick it stays wrong.
        if (Math.Abs(scale - _reported) < 0.001)
            return;

        _reported = scale;
        ScaleChanged?.Invoke(scale);
    }

    /// <summary>The scale of the focused monitor, or null when it cannot be read.</summary>
    public static double? ReadScale()
    {
        var json = Query("j/monitors");

        if (json is null)
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var monitors = document.RootElement.EnumerateArray().ToList();

            if (monitors.Count == 0)
                return null;

            var monitor = monitors.FirstOrDefault(
                m => m.TryGetProperty("focused", out var focused) && focused.GetBoolean());

            if (monitor.ValueKind == JsonValueKind.Undefined)
                monitor = monitors[0];

            return monitor.TryGetProperty("scale", out var scale) ? scale.GetDouble() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? Query(string command)
    {
        if (SocketPath is not { } path)
            return null;

        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(path));
            socket.Send(Encoding.UTF8.GetBytes(command));

            var buffer = new byte[64 * 1024];
            var response = new StringBuilder();
            int read;

            while ((read = socket.Receive(buffer)) > 0)
                response.Append(Encoding.UTF8.GetString(buffer, 0, read));

            return response.ToString();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _timer.Stop();
}
