using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace VisualSidekick.Server;

public sealed class CopilotAnalysisService : IAsyncDisposable
{
    private const int MaxImagesPerSession = 7;
    private const int MaxContinuityCharacters = 6000;
    private const string SystemPrompt = """
        You are Visual Sidekick, a live review companion. The user explicitly selected the only window you may inspect.

        For automatic screen updates:
        - Respond with one or two concise sentences.
        - If the screen contains a question, give the best answer and the reason.
        - If it is a presentation slide, give a pointed view on the message, clarity, and any obvious weakness.
        - If it is an error or application interface, explain what matters and the most useful next action.
        - Do not narrate obvious visual details or ask what the user wants.

        For typed follow-up questions, answer directly using the current screenshot and conversation context.
        Never claim to see content that is not visible in the supplied image.
        If the view appears to contain credentials or highly sensitive personal data, advise the user to pause.
        If the view appears to be a graded assessment, do not provide answers. Offer high-level learning guidance instead.
        """;

    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly string _model;
    private readonly Queue<string> _continuityHistory = new();
    private CopilotClient? _client;
    private CopilotSession? _session;
    private string? _runtimeDirectory;
    private int[] _runtimeProcessIds = [];
    private int _imagesInSession;
    private int _sessionRotationCount;
    private bool _includeContinuityOnNextRequest;
    private bool _disposed;

    public CopilotAnalysisService(string? model = null)
    {
        _model = ModelConfiguration.Resolve(model);
    }

    public int SessionRotationCount => _sessionRotationCount;

    public async Task<string> AnalyzeAsync(
        byte[] pngBytes,
        string prompt,
        CancellationToken cancellationToken,
        Action<string>? onDelta = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(pngBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            if (_imagesInSession >= MaxImagesPerSession)
            {
                await RotateSessionAsync(cancellationToken);
            }

            var (imageBytes, mimeType) = PrepareImage(pngBytes);
            var effectivePrompt = BuildPrompt(prompt);
            using var subscription = _session!.On<AssistantMessageDeltaEvent>(message =>
            {
                var delta = message.Data?.DeltaContent;
                if (!string.IsNullOrEmpty(delta))
                {
                    onDelta?.Invoke(delta);
                }
            });
            var response = await _session!.SendAndWaitAsync(
                new MessageOptions
                {
                    Prompt = effectivePrompt,
                    Attachments =
                    [
                        new AttachmentBlob
                        {
                            Data = Convert.ToBase64String(imageBytes),
                            MimeType = mimeType,
                            DisplayName = "observed-window.jpg"
                        }
                    ]
                },
                TimeSpan.FromMinutes(2),
                cancellationToken);

            var content = response?.Data?.Content?.Trim();
            var finalContent = string.IsNullOrWhiteSpace(content)
                ? "Copilot returned no commentary for this view."
                : content;
            _imagesInSession++;
            AddContinuityEntry(prompt, finalContent);
            return finalContent;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async Task AbortAsync()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        try
        {
            await session.AbortAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // A completed or starting request doesn't need an abort.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopRuntimeProcesses();
        var gateAcquired = await _requestGate.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            _session = null;
            _client = null;

            if (_runtimeDirectory is not null && Directory.Exists(_runtimeDirectory))
            {
                await DeleteRuntimeDirectoryAsync(_runtimeDirectory);
            }
        }
        finally
        {
            if (gateAcquired)
            {
                _requestGate.Release();
                _requestGate.Dispose();
            }
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_session is not null)
        {
            return;
        }

        _runtimeDirectory = Path.Combine(
            Path.GetTempPath(),
            "visual-sidekick",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_runtimeDirectory);

        _client = new CopilotClient(new CopilotClientOptions
        {
            Mode = CopilotClientMode.Empty,
            BaseDirectory = _runtimeDirectory,
            WorkingDirectory = AppContext.BaseDirectory,
            UseLoggedInUser = true,
            EnableRemoteSessions = false,
            LogLevel = CopilotLogLevel.Error
        });
        var existingRuntimeProcessIds = FindBundledRuntimeProcesses();
        await _client.StartAsync(cancellationToken);

        _session = await CreateSessionAsync(cancellationToken);
        _runtimeProcessIds = FindBundledRuntimeProcesses()
            .Except(existingRuntimeProcessIds)
            .ToArray();
    }

    private async Task<CopilotSession> CreateSessionAsync(CancellationToken cancellationToken)
    {
        return await _client!.CreateSessionAsync(
            new SessionConfig
            {
                ClientName = "Visual Sidekick",
                Model = _model,
                Streaming = true,
                AvailableTools = [],
                OnPermissionRequest = (_, _) => Task.FromResult(
                    PermissionDecision.Reject("Visual Sidekick does not allow runtime tool access.")),
                SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Replace,
                    Content = SystemPrompt
                },
                EnableConfigDiscovery = false,
                EnableOnDemandInstructionDiscovery = false,
                EnableSessionStore = false,
                EnableSkills = false,
                EnableFileHooks = false,
                EnableHostGitOperations = false,
                SkipEmbeddingRetrieval = true,
                EmbeddingCacheStorage = EmbeddingCacheStorageMode.InMemory
            },
            cancellationToken);
    }

    private async Task RotateSessionAsync(CancellationToken cancellationToken)
    {
        var previousSession = _session;
        _session = await CreateSessionAsync(cancellationToken);
        _imagesInSession = 0;
        _sessionRotationCount++;
        _includeContinuityOnNextRequest = _continuityHistory.Count > 0;

        if (previousSession is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await previousSession.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch
                {
                    // The new clean session is active; old temporary state is removed on shutdown.
                }
            });
        }
    }

    private string BuildPrompt(string prompt)
    {
        if (!_includeContinuityOnNextRequest || _continuityHistory.Count == 0)
        {
            return prompt;
        }

        _includeContinuityOnNextRequest = false;
        return "Text-only context retained from the earlier visual conversation:" +
               Environment.NewLine +
               string.Join(Environment.NewLine + Environment.NewLine, _continuityHistory) +
               Environment.NewLine +
               Environment.NewLine +
               "Current request:" +
               Environment.NewLine +
               prompt;
    }

    private void AddContinuityEntry(string prompt, string response)
    {
        var label = prompt.StartsWith(
            "The observed window changed.",
            StringComparison.Ordinal)
            ? "Earlier screen observation"
            : $"User asked: {prompt}";
        _continuityHistory.Enqueue($"{label}{Environment.NewLine}Copilot answered: {response}");

        while (
            _continuityHistory.Count > 1 &&
            _continuityHistory.Sum(entry => entry.Length) > MaxContinuityCharacters)
        {
            _continuityHistory.Dequeue();
        }
    }

    private static (byte[] Bytes, string MimeType) PrepareImage(byte[] pngBytes)
    {
        const int maxWidth = 1200;
        const int maxHeight = 1000;
        const long jpegQuality = 82L;

        using var input = new MemoryStream(pngBytes);
        using var source = new Bitmap(input);
        var scale = Math.Min(
            1d,
            Math.Min(
                (double)maxWidth / source.Width,
                (double)maxHeight / source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var prepared = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(prepared))
        {
            graphics.Clear(Color.White);
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        }

        using var output = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders()
            .Single(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, jpegQuality);
        prepared.Save(output, encoder, parameters);
        return (output.ToArray(), "image/jpeg");
    }

    private static async Task DeleteRuntimeDirectoryAsync(string path)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (
                attempt < 5 &&
                (ex is IOException || ex is UnauthorizedAccessException))
            {
                await Task.Delay(200 * attempt);
            }
        }

        if (Directory.Exists(path))
        {
            Console.Error.WriteLine($"Visual Sidekick could not remove temporary runtime storage: {path}");
        }
    }

    private static int[] FindBundledRuntimeProcesses()
    {
        var runtimeIdentifier = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "win-arm64",
            Architecture.X64 => "win-x64",
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(runtimeIdentifier))
        {
            return [];
        }

        var expectedPath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "runtimes",
                runtimeIdentifier,
                "native",
                "copilot.exe"));

        var processIds = new List<int>();
        foreach (var process in Process.GetProcessesByName("copilot"))
        {
            try
            {
                if (string.Equals(
                    Path.GetFullPath(process.MainModule?.FileName ?? string.Empty),
                    expectedPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    processIds.Add(process.Id);
                }
            }
            catch
            {
                // Some protected processes don't expose their executable path.
            }
            finally
            {
                process.Dispose();
            }
        }

        return processIds.ToArray();
    }

    private void StopRuntimeProcesses()
    {
        foreach (var processId in _runtimeProcessIds)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch (ArgumentException)
            {
                // The runtime already exited.
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"Visual Sidekick could not stop Copilot runtime process {processId}: {ex.Message}");
            }
        }

        _runtimeProcessIds = [];
    }
}
