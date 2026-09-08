using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace VisualSidekick.Server;

[McpServerToolType]
public sealed class VisualSidekickTools(
    WindowCaptureService capture,
    LiveObserverManager live)
{
    private static readonly JsonSerializerOptions StatusJsonOptions = new()
    {
        WriteIndented = true
    };

    [McpServerTool(Name = "start", Title = "Start Visual Sidekick")]
    [Description("Opens the Windows picker and starts a session for the window explicitly selected by the user.")]
    public string Start(
        [Description("Local capture interval in milliseconds, from 250 to 30000.")] int captureIntervalMilliseconds = 1000)
    {
        try
        {
            var status = capture.Start(null, null, captureIntervalMilliseconds);
            return $"Visual Sidekick started for '{status.WindowTitle}' ({status.ProcessName}). " +
                   $"Frames are captured locally every {status.CaptureIntervalMilliseconds} ms.";
        }
        catch (Exception ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "start_live", Title = "Start live Visual Sidekick")]
    [Description("Opens the Windows picker, then starts a visible companion for the window explicitly selected by the user.")]
    public string StartLive(
        [Description("Local capture interval in milliseconds, from 250 to 30000.")] int captureIntervalMilliseconds = 500,
        [Description("Optional vision-capable Copilot model. Uses VISUAL_SIDEKICK_MODEL or the built-in default when omitted.")] string? model = null)
    {
        try
        {
            var status = capture.Start(null, null, captureIntervalMilliseconds);
            try
            {
                live.Start(ModelConfiguration.Resolve(model));
            }
            catch
            {
                capture.Stop();
                throw;
            }

            var selectedModel = ModelConfiguration.Resolve(model);
            return $"Visual Sidekick started for '{status.WindowTitle}' ({status.ProcessName}) using {selectedModel}. " +
                   "The companion panel will respond automatically after meaningful visual changes.";
        }
        catch (Exception ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "select_region", Title = "Select observed region")]
    [Description("Shows the current captured window and lets the user drag over the region Copilot should observe.")]
    public string SelectRegion()
    {
        try
        {
            var status = capture.SelectRegion();
            return status.Region is null
                ? "The full window is being observed."
                : $"Observed region set to x={status.Region.Value.X}, y={status.Region.Value.Y}, " +
                  $"width={status.Region.Value.Width}, height={status.Region.Value.Height}.";
        }
        catch (Exception ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "capture_current", Title = "Capture current observed view")]
    [Description("Returns the latest image from the selected window. Use this before answering questions about what is currently visible.")]
    public IEnumerable<ContentBlock> CaptureCurrent()
    {
        try
        {
            var frame = capture.GetCurrentFrame(forceCapture: true);
            return
            [
                new TextContentBlock
                {
                    Text = $"Captured at {frame.CapturedAt:O}. Size {frame.Width}x{frame.Height}. " +
                           $"Method: {frame.CaptureMethod}. Visual change distance: {frame.ChangeDistance}."
                },
                ImageContentBlock.FromBytes(frame.PngBytes, "image/png")
            ];
        }
        catch (Exception ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "capture_changes", Title = "Compare observed views")]
    [Description("Returns the previous significant keyframe and the current view to help explain what changed.")]
    public IEnumerable<ContentBlock> CaptureChanges()
    {
        try
        {
            var (previous, current) = capture.GetFrameComparison();
            var blocks = new List<ContentBlock>();

            if (previous is not null)
            {
                blocks.Add(new TextContentBlock
                {
                    Text = $"Previous keyframe captured at {previous.CapturedAt:O}."
                });
                blocks.Add(ImageContentBlock.FromBytes(previous.PngBytes, "image/png"));
            }
            else
            {
                blocks.Add(new TextContentBlock
                {
                    Text = "No previous significant keyframe is available yet."
                });
            }

            blocks.Add(new TextContentBlock
            {
                Text = $"Current view captured at {current.CapturedAt:O}. Visual change distance: {current.ChangeDistance}."
            });
            blocks.Add(ImageContentBlock.FromBytes(current.PngBytes, "image/png"));
            return blocks;
        }
        catch (Exception ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "status", Title = "Visual Sidekick status")]
    [Description("Returns the active window, region, capture interval, last capture, change count, and latest error.")]
    public string Status() =>
        JsonSerializer.Serialize(
            new
            {
                LivePanelActive = live.IsRunning,
                Capture = capture.GetStatus()
            },
            StatusJsonOptions);

    [McpServerTool(Name = "pause", Title = "Pause screen observation")]
    [Description("Pauses local frame capture without clearing the selected window or buffered frames.")]
    public string Pause()
    {
        try
        {
            if (live.IsRunning)
            {
                live.SetPaused(true);
            }
            else
            {
                capture.Pause();
            }

            return "Visual Sidekick paused.";
        }
        catch (Exception ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "resume", Title = "Resume screen observation")]
    [Description("Resumes local frame capture for the selected window.")]
    public string Resume()
    {
        try
        {
            if (live.IsRunning)
            {
                live.SetPaused(false);
            }
            else
            {
                capture.Resume();
            }

            return "Visual Sidekick resumed.";
        }
        catch (Exception ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "set_interval", Title = "Set capture interval")]
    [Description("Changes how frequently frames are captured locally while observation is active.")]
    public string SetInterval(
        [Description("Capture interval in milliseconds, from 250 to 30000.")] int captureIntervalMilliseconds)
    {
        try
        {
            capture.SetInterval(captureIntervalMilliseconds);
            return $"Capture interval set to {captureIntervalMilliseconds} ms.";
        }
        catch (Exception ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "stop", Title = "Stop screen observation")]
    [Description("Stops observation and clears all captured images and window state from memory.")]
    public string Stop()
    {
        if (live.IsRunning)
        {
            live.StopAsync(stopCapture: false).GetAwaiter().GetResult();
        }

        capture.Stop();
        return "Visual Sidekick stopped and buffered frames were cleared.";
    }
}
