namespace VisualSidekick.Server;

public sealed class LiveObserverManager : IAsyncDisposable
{
    private const int SettleDelayMilliseconds = 650;

    private readonly WindowCaptureService _capture;
    private readonly object _stateLock = new();
    private LiveObserverForm? _form;
    private Thread? _uiThread;
    private CopilotAnalysisService? _analysis;
    private CancellationTokenSource? _lifetime;
    private CancellationTokenSource? _debounce;
    private long _changeGeneration;
    private int _analysisInFlight;
    private bool _running;
    private bool _paused;
    private bool _stopping;

    public LiveObserverManager(WindowCaptureService capture)
    {
        _capture = capture;
    }

    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _running;
            }
        }
    }

    public void Start(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        lock (_stateLock)
        {
            if (_running)
            {
                throw new InvalidOperationException("Live observation is already running.");
            }

            var status = _capture.GetStatus();
            if (!status.Active)
            {
                throw new InvalidOperationException("Start screen observation before opening live mode.");
            }

            _analysis = new CopilotAnalysisService(model);
            _lifetime = new CancellationTokenSource();
            _stopping = false;
            _running = true;
            _paused = false;
        }

        var ready = new ManualResetEventSlim();
        Exception? uiFailure = null;

        _uiThread = new Thread(() =>
        {
            try
            {
                var status = _capture.GetStatus();
                using var form = new LiveObserverForm(status.WindowTitle ?? "Observed window");
                form.QuestionSubmitted += (_, question) => _ = SubmitQuestionAsync(question);
                form.PauseRequested += (_, _) => TogglePause();
                form.StopRequested += (_, _) => _ = StopAsync(stopCapture: true);
                form.FormClosed += (_, _) => _ = StopAsync(stopCapture: true);

                lock (_stateLock)
                {
                    _form = form;
                }

                ready.Set();
                Application.Run(form);
            }
            catch (Exception ex)
            {
                uiFailure = ex;
                ready.Set();
            }
        })
        {
            IsBackground = true,
            Name = "Visual Sidekick UI"
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
        ready.Wait();

        if (uiFailure is not null)
        {
            lock (_stateLock)
            {
                _running = false;
            }

            throw new InvalidOperationException("Unable to open the live observer panel.", uiFailure);
        }

        _capture.SignificantFrameChanged += OnSignificantFrameChanged;
        _ = WarmUpAsync();
        QueueAutomaticAnalysis();
    }

    public async Task StopAsync(bool stopCapture)
    {
        LiveObserverForm? form;
        CopilotAnalysisService? analysis;
        CancellationTokenSource? lifetime;

        lock (_stateLock)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            form = _form;
            analysis = _analysis;
            lifetime = _lifetime;
            _form = null;
            _analysis = null;
            _lifetime = null;
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = null;
        }

        try
        {
            _capture.SignificantFrameChanged -= OnSignificantFrameChanged;
            lifetime?.Cancel();

            if (form is not null && !form.IsDisposed)
            {
                form.BeginInvoke(form.Close);
            }

            if (analysis is not null)
            {
                await analysis.DisposeAsync();
            }

            if (stopCapture)
            {
                _capture.Stop();
            }
        }
        finally
        {
            lifetime?.Dispose();
            lock (_stateLock)
            {
                _stopping = false;
                _running = false;
                _paused = false;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(stopCapture: false);
    }

    public void SetPaused(bool paused)
    {
        lock (_stateLock)
        {
            if (_form is null || _form.IsDisposed)
            {
                throw new InvalidOperationException("Live observation is not running.");
            }

            _paused = paused;
            if (paused)
            {
                _debounce?.Cancel();
            }
        }

        if (paused)
        {
            _capture.Pause();
        }
        else
        {
            _capture.Resume();
            QueueAutomaticAnalysis();
        }

        GetForm()?.SetPaused(paused);
    }

    private void OnSignificantFrameChanged(object? sender, FrameSnapshot frame)
    {
        if (Volatile.Read(ref _analysisInFlight) == 1)
        {
            GetForm()?.CancelCopilotResponse();
            _ = GetAnalysis().AbortAsync();
        }

        QueueAutomaticAnalysis();
    }

    private void QueueAutomaticAnalysis()
    {
        CancellationToken token;
        long generation;

        lock (_stateLock)
        {
            if (_paused || _stopping || _lifetime is null)
            {
                return;
            }

            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            token = _debounce.Token;
            generation = ++_changeGeneration;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SettleDelayMilliseconds, token);
                var frame = _capture.GetCurrentFrame(forceCapture: false);
                SetBusy(true);
                Interlocked.Exchange(ref _analysisInFlight, 1);
                var response = await GetAnalysis().AnalyzeAsync(
                    frame.PngBytes,
                    "The observed window changed. Review this current view automatically.",
                    token,
                    delta => GetForm()?.AppendCopilotDelta(delta));

                lock (_stateLock)
                {
                    if (generation != _changeGeneration || _stopping)
                    {
                        return;
                    }
                }

                GetForm()?.CompleteCopilotResponse(response);
                GetForm()?.SetStatus("Watching for changes...");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                GetForm()?.AppendError(ex.Message);
                GetForm()?.SetStatus("Analysis failed. Watching continues.");
            }
            finally
            {
                Interlocked.Exchange(ref _analysisInFlight, 0);
                SetBusy(false);
            }
        }, token);
    }

    private async Task SubmitQuestionAsync(string question)
    {
        CancellationToken token;
        lock (_stateLock)
        {
            _debounce?.Cancel();
            _changeGeneration++;
            token = _lifetime?.Token
                ?? throw new InvalidOperationException("Live observation is not running.");
        }

        try
        {
            SetBusy(true);
            Interlocked.Exchange(ref _analysisInFlight, 1);
            var frame = _capture.GetCurrentFrame(forceCapture: false);
            var response = await GetAnalysis().AnalyzeAsync(
                frame.PngBytes,
                question,
                token,
                delta => GetForm()?.AppendCopilotDelta(delta));
            GetForm()?.CompleteCopilotResponse(response);
            GetForm()?.SetStatus("Watching for changes...");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            GetForm()?.AppendError(ex.Message);
            GetForm()?.SetStatus("Question failed. Watching continues.");
        }
        finally
        {
            Interlocked.Exchange(ref _analysisInFlight, 0);
            SetBusy(false);
        }
    }

    private void TogglePause()
    {
        try
        {
            bool paused;
            lock (_stateLock)
            {
                paused = !_paused;
            }

            SetPaused(paused);
        }
        catch (Exception ex)
        {
            GetForm()?.AppendError(ex.Message);
        }
    }

    private CopilotAnalysisService GetAnalysis()
    {
        lock (_stateLock)
        {
            return _analysis
                ?? throw new InvalidOperationException("Live analysis is not running.");
        }
    }

    private LiveObserverForm? GetForm()
    {
        lock (_stateLock)
        {
            return _form;
        }
    }

    private void SetBusy(bool busy)
    {
        GetForm()?.SetBusy(busy);
    }

    private async Task WarmUpAsync()
    {
        try
        {
            var token = _lifetime?.Token ?? CancellationToken.None;
            GetForm()?.SetStatus("Preparing the vision session...");
            await GetAnalysis().WarmUpAsync(token);
            GetForm()?.SetStatus("Watching for changes...");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            GetForm()?.AppendError($"Vision session startup failed: {ex.Message}");
        }
    }
}
