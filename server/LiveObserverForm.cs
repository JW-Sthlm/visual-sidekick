namespace VisualSidekick.Server;

public sealed class LiveObserverForm : Form
{
    private readonly Label _windowLabel;
    private readonly Label _statusLabel;
    private readonly RichTextBox _conversation;
    private readonly TextBox _input;
    private readonly Button _sendButton;
    private readonly Button _pauseButton;
    private bool _streamingResponseOpen;

    public LiveObserverForm(string windowTitle)
    {
        Text = "Visual Sidekick";
        Width = 520;
        Height = 780;
        MinimumSize = new Size(420, 560);
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = true;

        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        if (Environment.GetEnvironmentVariable("SCREEN_OBSERVER_TEST_OFFSCREEN") == "1")
        {
            Left = -4000;
            Top = -4000;
            ShowInTaskbar = false;
        }
        else
        {
            Left = Math.Max(workingArea.Left, workingArea.Right - Width - 20);
            Top = workingArea.Top + 20;
        }

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 88,
            Padding = new Padding(12, 10, 12, 8),
            BackColor = Color.FromArgb(246, 248, 250)
        };

        _windowLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 38,
            Font = new Font("Segoe UI Semibold", 10),
            AutoEllipsis = true,
            Text = windowTitle
        };
        _statusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            ForeColor = Color.FromArgb(65, 75, 85),
            Text = "Starting live observation..."
        };
        header.Controls.Add(_statusLabel);
        header.Controls.Add(_windowLabel);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(8, 7, 8, 5),
            FlowDirection = FlowDirection.LeftToRight
        };
        _pauseButton = new Button { Text = "Pause", Width = 90 };
        var stopButton = new Button { Text = "Stop", Width = 90 };
        _pauseButton.Click += (_, _) => PauseRequested?.Invoke(this, EventArgs.Empty);
        stopButton.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        toolbar.Controls.Add(_pauseButton);
        toolbar.Controls.Add(stopButton);

        _conversation = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            Font = new Font("Segoe UI", 10.5f),
            DetectUrls = true,
            HideSelection = false
        };

        var composer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 112,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(246, 248, 250)
        };
        _sendButton = new Button
        {
            Dock = DockStyle.Right,
            Width = 84,
            Text = "Send"
        };
        _input = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            Font = new Font("Segoe UI", 10.5f),
            ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "Ask a follow-up question. Ctrl+Enter sends."
        };
        _sendButton.Click += (_, _) => SubmitQuestion();
        _input.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Control && eventArgs.KeyCode == Keys.Enter)
            {
                eventArgs.SuppressKeyPress = true;
                SubmitQuestion();
            }
        };
        composer.Controls.Add(_input);
        composer.Controls.Add(_sendButton);

        Controls.Add(_conversation);
        Controls.Add(composer);
        Controls.Add(toolbar);
        Controls.Add(header);

        AppendSystem("Watching for meaningful visual changes. Commentary appears automatically after the view settles.");
    }

    public event EventHandler<string>? QuestionSubmitted;
    public event EventHandler? PauseRequested;
    public event EventHandler? StopRequested;

    public void SetStatus(string text)
    {
        RunOnUi(() => _statusLabel.Text = text);
    }

    public void SetPaused(bool paused)
    {
        RunOnUi(() =>
        {
            _pauseButton.Text = paused ? "Resume" : "Pause";
            _statusLabel.Text = paused ? "Paused" : "Watching for changes...";
        });
    }

    public void SetBusy(bool busy)
    {
        RunOnUi(() =>
        {
            if (busy)
            {
                _statusLabel.Text = "Analyzing current view...";
            }
        });
    }

    public void AppendAutomatic(string text) =>
        AppendEntry("Sidekick", text, Color.FromArgb(9, 105, 218));

    public void AppendCopilotDelta(string text)
    {
        RunOnUi(() =>
        {
            if (!_streamingResponseOpen)
            {
                AppendHeader("Sidekick", Color.FromArgb(9, 105, 218));
                _streamingResponseOpen = true;
            }

            _conversation.SelectionFont = _conversation.Font;
            _conversation.SelectionColor = Color.FromArgb(31, 35, 40);
            _conversation.AppendText(text);
            _conversation.SelectionStart = _conversation.TextLength;
            _conversation.ScrollToCaret();
        });
    }

    public void CompleteCopilotResponse(string fallbackText)
    {
        RunOnUi(() =>
        {
            if (!_streamingResponseOpen)
            {
                AppendEntryCore("Sidekick", fallbackText, Color.FromArgb(9, 105, 218));
                return;
            }

            _conversation.AppendText(Environment.NewLine + Environment.NewLine);
            _streamingResponseOpen = false;
            _conversation.SelectionStart = _conversation.TextLength;
            _conversation.ScrollToCaret();
        });
    }

    public void CancelCopilotResponse()
    {
        RunOnUi(() =>
        {
            if (!_streamingResponseOpen)
            {
                return;
            }

            _conversation.SelectionColor = Color.FromArgb(87, 96, 106);
            _conversation.AppendText(" [newer view detected]" + Environment.NewLine + Environment.NewLine);
            _streamingResponseOpen = false;
            _conversation.SelectionStart = _conversation.TextLength;
            _conversation.ScrollToCaret();
        });
    }

    public void AppendQuestion(string text) =>
        AppendEntry("You", text, Color.FromArgb(87, 96, 106));

    public void AppendError(string text) =>
        AppendEntry("Error", text, Color.FromArgb(207, 34, 46));

    public void AppendSystem(string text) =>
        AppendEntry("Visual Sidekick", text, Color.FromArgb(111, 66, 193));

    private void SubmitQuestion()
    {
        var question = _input.Text.Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            return;
        }

        _input.Clear();
        AppendQuestion(question);
        QuestionSubmitted?.Invoke(this, question);
    }

    private void AppendEntry(string role, string text, Color roleColor)
    {
        RunOnUi(() => AppendEntryCore(role, text, roleColor));
    }

    private void AppendEntryCore(string role, string text, Color roleColor)
    {
        AppendHeader(role, roleColor);
        _conversation.SelectionFont = _conversation.Font;
        _conversation.SelectionColor = Color.FromArgb(31, 35, 40);
        _conversation.AppendText(text.Trim() + Environment.NewLine + Environment.NewLine);
        _conversation.SelectionStart = _conversation.TextLength;
        _conversation.ScrollToCaret();
    }

    private void AppendHeader(string role, Color roleColor)
    {
        _conversation.SelectionStart = _conversation.TextLength;
        _conversation.SelectionFont = new Font(_conversation.Font, FontStyle.Bold);
        _conversation.SelectionColor = roleColor;
        _conversation.AppendText($"{role}  {DateTime.Now:HH:mm:ss}{Environment.NewLine}");
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
    }
}
