using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace VisualSidekick.Server;

public sealed class WindowCaptureService : IDisposable
{
    private const int DefaultIntervalMilliseconds = 1000;
    private const int MinimumIntervalMilliseconds = 250;
    private const int MaximumIntervalMilliseconds = 30000;
    private const int MaxImageWidth = 1600;
    private const int MaxImageHeight = 1200;

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private System.Threading.Timer? _timer;
    private nint _windowHandle;
    private WindowInfo? _window;
    private Rectangle? _region;
    private FrameSnapshot? _currentFrame;
    private FrameSnapshot? _previousFrame;
    private bool _paused;
    private int _captureIntervalMilliseconds = DefaultIntervalMilliseconds;
    private long _changeCount;
    private long _observationVersion;
    private string? _lastError;
    private bool _disposed;

    public event EventHandler<FrameSnapshot>? SignificantFrameChanged;

    public IReadOnlyList<WindowInfo> ListWindows()
    {
        var windows = new List<WindowInfo>();
        var ownProcessId = Environment.ProcessId;

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle) || NativeMethods.IsCloaked(handle))
            {
                return true;
            }

            var titleLength = NativeMethods.GetWindowTextLength(handle);
            if (titleLength <= 0 || !NativeMethods.GetWindowRect(handle, out var rect))
            {
                return true;
            }

            if (rect.Width < 200 || rect.Height < 100)
            {
                return true;
            }

            var titleBuffer = new StringBuilder(titleLength + 1);
            NativeMethods.GetWindowText(handle, titleBuffer, titleBuffer.Capacity);
            var title = titleBuffer.ToString().Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            if (processId == ownProcessId)
            {
                return true;
            }

            var processName = "unknown";
            try
            {
                processName = Process.GetProcessById((int)processId).ProcessName;
            }
            catch
            {
                // The window may disappear while it is being enumerated.
            }

            windows.Add(new WindowInfo(
                handle.ToInt64(),
                title,
                processName,
                rect.Left,
                rect.Top,
                rect.Width,
                rect.Height,
                NativeMethods.IsIconic(handle)));

            return true;
        }, nint.Zero);

        return windows
            .OrderBy(window => window.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ObserverStatus Start(
        string? titleContains,
        long? windowId,
        int captureIntervalMilliseconds,
        bool showPicker = true)
    {
        ThrowIfDisposed();
        ValidateInterval(captureIntervalMilliseconds);

        var windows = ListWindows();
        WindowInfo? selected;

        if (windowId is not null)
        {
            selected = windows.SingleOrDefault(window => window.Id == windowId.Value);
            if (selected is null)
            {
                throw new InvalidOperationException($"Window ID {windowId.Value} is no longer available.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(titleContains))
        {
            var matches = windows
                .Where(window => window.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            selected = matches.Length switch
            {
                0 => throw new InvalidOperationException($"No visible window title contains '{titleContains}'."),
                1 => matches[0],
                _ => throw new InvalidOperationException(
                    $"More than one window matches '{titleContains}': " +
                    string.Join("; ", matches.Take(8).Select(window => $"{window.Id} {window.Title}")))
            };
        }
        else if (showPicker)
        {
            selected = WindowPicker.Pick(windows);
            if (selected is null)
            {
                throw new OperationCanceledException("Window selection was cancelled.");
            }
        }
        else
        {
            throw new InvalidOperationException("Provide a window title or window ID.");
        }

        StopTimer();
        lock (_stateLock)
        {
            _windowHandle = new nint(selected.Id);
            _window = selected;
            _region = null;
            _currentFrame = null;
            _previousFrame = null;
            _paused = false;
            _captureIntervalMilliseconds = captureIntervalMilliseconds;
            _changeCount = 0;
            _lastError = null;
            _observationVersion++;
        }

        CaptureAndStore(forceChange: true);
        StartTimer();
        return GetStatus();
    }

    public ObserverStatus Pause()
    {
        ThrowIfDisposed();
        lock (_stateLock)
        {
            EnsureActive();
            _paused = true;
        }

        return GetStatus();
    }

    public ObserverStatus Resume()
    {
        ThrowIfDisposed();
        lock (_stateLock)
        {
            EnsureActive();
            _paused = false;
        }

        CaptureAndStore(forceChange: false);
        return GetStatus();
    }

    public ObserverStatus Stop()
    {
        ThrowIfDisposed();
        StopTimer();
        lock (_stateLock)
        {
            _windowHandle = nint.Zero;
            _window = null;
            _region = null;
            _currentFrame = null;
            _previousFrame = null;
            _paused = false;
            _changeCount = 0;
            _lastError = null;
            _observationVersion++;
        }

        return GetStatus();
    }

    public ObserverStatus SetInterval(int captureIntervalMilliseconds)
    {
        ThrowIfDisposed();
        ValidateInterval(captureIntervalMilliseconds);

        lock (_stateLock)
        {
            EnsureActive();
            _captureIntervalMilliseconds = captureIntervalMilliseconds;
        }

        StartTimer();
        return GetStatus();
    }

    public ObserverStatus SelectRegion()
    {
        ThrowIfDisposed();
        nint handle;
        lock (_stateLock)
        {
            EnsureActive();
            handle = _windowHandle;
        }

        using var fullWindow = CaptureBitmap(handle, region: null, out _);
        var selection = RegionPicker.Pick(fullWindow);
        if (!selection.Accepted)
        {
            return GetStatus();
        }

        lock (_stateLock)
        {
            _region = selection.Region;
            _currentFrame = null;
            _previousFrame = null;
            _changeCount = 0;
            _observationVersion++;
        }

        CaptureAndStore(forceChange: true);
        return GetStatus();
    }

    public FrameSnapshot GetCurrentFrame(bool forceCapture)
    {
        ThrowIfDisposed();
        lock (_stateLock)
        {
            EnsureActive();
            if (_paused)
            {
                throw new InvalidOperationException("Screen observation is paused.");
            }
        }

        if (forceCapture)
        {
            CaptureAndStore(forceChange: false);
        }

        lock (_stateLock)
        {
            return _currentFrame
                ?? throw new InvalidOperationException(_lastError ?? "No frame has been captured yet.");
        }
    }

    public (FrameSnapshot? Previous, FrameSnapshot Current) GetFrameComparison()
    {
        var current = GetCurrentFrame(forceCapture: true);
        lock (_stateLock)
        {
            return (_previousFrame, current);
        }
    }

    public ObserverStatus GetStatus()
    {
        lock (_stateLock)
        {
            return new ObserverStatus(
                _window is not null,
                _paused,
                _window?.Id,
                _window?.Title,
                _window?.ProcessName,
                _captureIntervalMilliseconds,
                _region,
                _currentFrame?.CapturedAt,
                _changeCount,
                _lastError);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopTimer();
        _captureGate.Wait();
        _captureGate.Release();
        _captureGate.Dispose();
    }

    private void StartTimer()
    {
        StopTimer();
        int interval;
        lock (_stateLock)
        {
            if (_window is null)
            {
                return;
            }

            interval = _captureIntervalMilliseconds;
        }

        _timer = new System.Threading.Timer(_ => ObserveTimerTick(), null, interval, interval);
    }

    private void StopTimer()
    {
        var timer = Interlocked.Exchange(ref _timer, null);
        timer?.Dispose();
    }

    private void ObserveTimerTick()
    {
        if (!_captureGate.Wait(0))
        {
            return;
        }

        try
        {
            bool shouldCapture;
            lock (_stateLock)
            {
                shouldCapture = _window is not null && !_paused;
            }

            if (shouldCapture)
            {
                CaptureAndStoreCore(forceChange: false);
            }
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                _lastError = ex.Message;
            }
        }
        finally
        {
            _captureGate.Release();
        }
    }

    private void CaptureAndStore(bool forceChange)
    {
        _captureGate.Wait();
        try
        {
            CaptureAndStoreCore(forceChange);
        }
        finally
        {
            _captureGate.Release();
        }
    }

    private void CaptureAndStoreCore(bool forceChange)
    {
        nint handle;
        Rectangle? region;
        long observationVersion;
        lock (_stateLock)
        {
            EnsureActive();
            handle = _windowHandle;
            region = _region;
            observationVersion = _observationVersion;
        }

        if (!NativeMethods.IsWindow(handle))
        {
            throw new InvalidOperationException("The selected window is no longer available.");
        }

        using var bitmap = CaptureBitmap(handle, region, out var method);
        using var normalized = ResizeForModel(bitmap);
        var png = EncodePng(normalized);
        var signature = VisualChangeDetector.ComputeSignature(normalized);
        var now = DateTimeOffset.UtcNow;
        FrameSnapshot? changedFrame = null;

        lock (_stateLock)
        {
            if (_window is null || observationVersion != _observationVersion)
            {
                return;
            }

            var distance = _currentFrame is null
                ? 255
                : VisualChangeDetector.ComputeDistance(_currentFrame.VisualSignature, signature);
            var changed = forceChange || _currentFrame is null || VisualChangeDetector.IsMeaningful(distance);

            var next = new FrameSnapshot(
                png,
                signature,
                now,
                normalized.Width,
                normalized.Height,
                method,
                distance);

            if (changed && _currentFrame is not null)
            {
                _previousFrame = _currentFrame;
                _changeCount++;
            }

            _currentFrame = next;
            _lastError = null;
            if (changed)
            {
                changedFrame = next;
            }
        }

        if (changedFrame is not null)
        {
            SignificantFrameChanged?.Invoke(this, changedFrame);
        }
    }

    private static Bitmap CaptureBitmap(nint handle, Rectangle? region, out string captureMethod)
    {
        if (!NativeMethods.GetWindowRect(handle, out var bounds) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("Unable to read the selected window bounds.");
        }

        if (NativeMethods.IsIconic(handle))
        {
            throw new InvalidOperationException("The selected window is minimized. Restore it before capturing.");
        }

        var full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        var printSucceeded = false;

        using (var graphics = Graphics.FromImage(full))
        {
            graphics.Clear(Color.Black);
            var hdc = graphics.GetHdc();
            try
            {
                printSucceeded = NativeMethods.PrintWindow(handle, hdc, NativeMethods.PwRenderFullContent);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        if (printSucceeded && !IsMostlyBlack(full))
        {
            captureMethod = "PrintWindow";
        }
        else
        {
            try
            {
                using var graphics = Graphics.FromImage(full);
                graphics.CopyFromScreen(
                    bounds.Left,
                    bounds.Top,
                    0,
                    0,
                    new Size(bounds.Width, bounds.Height),
                    CopyPixelOperation.SourceCopy);
                captureMethod = "screen fallback";
            }
            catch (Exception ex)
            {
                full.Dispose();
                throw new InvalidOperationException(
                    "The selected window could not be captured. It may be protected or unavailable.",
                    ex);
            }
        }

        if (region is null)
        {
            return full;
        }

        var validRegion = Rectangle.Intersect(
            region.Value,
            new Rectangle(0, 0, full.Width, full.Height));
        if (validRegion.Width <= 0 || validRegion.Height <= 0)
        {
            full.Dispose();
            throw new InvalidOperationException("The selected region is outside the window.");
        }

        var cropped = full.Clone(validRegion, PixelFormat.Format32bppArgb);
        full.Dispose();
        return cropped;
    }

    private static Bitmap ResizeForModel(Bitmap source)
    {
        var scale = Math.Min(
            1d,
            Math.Min(
                (double)MaxImageWidth / source.Width,
                (double)MaxImageHeight / source.Height));

        if (scale >= 1d)
        {
            return new Bitmap(source);
        }

        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return resized;
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static bool IsMostlyBlack(Bitmap bitmap)
    {
        var darkSamples = 0;
        var totalSamples = 0;
        for (var y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 12))
        {
            for (var x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 12))
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R < 8 && pixel.G < 8 && pixel.B < 8)
                {
                    darkSamples++;
                }

                totalSamples++;
            }
        }

        return totalSamples > 0 && darkSamples >= totalSamples * 0.95;
    }

    private void EnsureActive()
    {
        if (_window is null || _windowHandle == nint.Zero)
        {
            throw new InvalidOperationException("No window is being observed. Start the observer first.");
        }
    }

    private static void ValidateInterval(int interval)
    {
        if (interval < MinimumIntervalMilliseconds || interval > MaximumIntervalMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                $"Capture interval must be between {MinimumIntervalMilliseconds} and {MaximumIntervalMilliseconds} milliseconds.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
