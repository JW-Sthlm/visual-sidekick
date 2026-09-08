using System.Drawing;

namespace VisualSidekick.Server;

public sealed record WindowInfo(
    long Id,
    string Title,
    string ProcessName,
    int Left,
    int Top,
    int Width,
    int Height,
    bool IsMinimized)
{
    public override string ToString() =>
        $"{ProcessName} | {Title} ({Width}x{Height})";
}

public sealed record FrameSnapshot(
    byte[] PngBytes,
    byte[] VisualSignature,
    DateTimeOffset CapturedAt,
    int Width,
    int Height,
    string CaptureMethod,
    int ChangeDistance);

public sealed record ObserverStatus(
    bool Active,
    bool Paused,
    long? WindowId,
    string? WindowTitle,
    string? ProcessName,
    int CaptureIntervalMilliseconds,
    Rectangle? Region,
    DateTimeOffset? LastCaptureAt,
    long ChangeCount,
    string? LastError);
