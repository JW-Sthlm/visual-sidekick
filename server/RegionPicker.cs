using System.Drawing.Drawing2D;

namespace VisualSidekick.Server;

internal static class RegionPicker
{
    internal static RegionSelectionResult Pick(Bitmap source)
    {
        var result = new RegionSelectionResult(false, null);
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var form = new RegionSelectionForm(source);
                if (form.ShowDialog() == DialogResult.OK)
                {
                    result = new RegionSelectionResult(true, form.SelectedImageRegion);
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("The region picker failed.", failure);
        }

        return result;
    }

    private sealed class RegionSelectionForm : Form
    {
        private readonly Bitmap _source;
        private readonly PictureBox _picture;
        private Point? _start;
        private Rectangle _selection;

        internal Rectangle? SelectedImageRegion { get; private set; }

        internal RegionSelectionForm(Bitmap source)
        {
            _source = new Bitmap(source);
            Text = "Visual Sidekick: drag to select a region";
            Width = 1200;
            Height = 850;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            MinimizeBox = false;

            var label = new Label
            {
                Dock = DockStyle.Top,
                Height = 42,
                Padding = new Padding(10),
                Text = "Drag over the area Visual Sidekick may inspect. Choose Use full window to clear an existing crop."
            };

            _picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30),
                Image = _source,
                SizeMode = PictureBoxSizeMode.Zoom,
                Cursor = Cursors.Cross
            };
            _picture.MouseDown += OnMouseDown;
            _picture.MouseMove += OnMouseMove;
            _picture.MouseUp += OnMouseUp;
            _picture.Paint += OnPaintSelection;

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 54,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8)
            };
            var cancel = new Button { Text = "Cancel", Width = 110, DialogResult = DialogResult.Cancel };
            var useSelection = new Button { Text = "Use selection", Width = 120 };
            var useFull = new Button { Text = "Use full window", Width = 130 };
            useSelection.Click += (_, _) => AcceptSelection();
            useFull.Click += (_, _) =>
            {
                SelectedImageRegion = null;
                DialogResult = DialogResult.OK;
                Close();
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(useSelection);
            buttons.Controls.Add(useFull);

            Controls.Add(_picture);
            Controls.Add(label);
            Controls.Add(buttons);
            CancelButton = cancel;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _source.Dispose();
            }

            base.Dispose(disposing);
        }

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !GetImageDisplayRectangle().Contains(e.Location))
            {
                return;
            }

            _start = e.Location;
            _selection = Rectangle.Empty;
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (_start is null)
            {
                return;
            }

            var display = GetImageDisplayRectangle();
            var current = new Point(
                Math.Clamp(e.X, display.Left, display.Right),
                Math.Clamp(e.Y, display.Top, display.Bottom));
            _selection = Normalize(_start.Value, current);
            _picture.Invalidate();
        }

        private void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (_start is null)
            {
                return;
            }

            _start = null;
            _picture.Invalidate();
        }

        private void OnPaintSelection(object? sender, PaintEventArgs e)
        {
            if (_selection.Width < 2 || _selection.Height < 2)
            {
                return;
            }

            using var fill = new SolidBrush(Color.FromArgb(55, 0, 120, 212));
            using var pen = new Pen(Color.DeepSkyBlue, 3) { DashStyle = DashStyle.Dash };
            e.Graphics.FillRectangle(fill, _selection);
            e.Graphics.DrawRectangle(pen, _selection);
        }

        private void AcceptSelection()
        {
            if (_selection.Width < 10 || _selection.Height < 10)
            {
                MessageBox.Show(
                    this,
                    "Drag a region of at least 10 by 10 pixels first.",
                    "Visual Sidekick",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var display = GetImageDisplayRectangle();
            var clipped = Rectangle.Intersect(_selection, display);
            var scaleX = (double)_source.Width / display.Width;
            var scaleY = (double)_source.Height / display.Height;

            var x = Math.Clamp(
                (int)Math.Round((clipped.Left - display.Left) * scaleX),
                0,
                _source.Width - 1);
            var y = Math.Clamp(
                (int)Math.Round((clipped.Top - display.Top) * scaleY),
                0,
                _source.Height - 1);
            var width = Math.Clamp(
                (int)Math.Round(clipped.Width * scaleX),
                1,
                _source.Width - x);
            var height = Math.Clamp(
                (int)Math.Round(clipped.Height * scaleY),
                1,
                _source.Height - y);

            SelectedImageRegion = new Rectangle(x, y, width, height);

            DialogResult = DialogResult.OK;
            Close();
        }

        private Rectangle GetImageDisplayRectangle()
        {
            var client = _picture.ClientRectangle;
            if (client.Width <= 0 || client.Height <= 0)
            {
                return Rectangle.Empty;
            }

            var imageRatio = (double)_source.Width / _source.Height;
            var clientRatio = (double)client.Width / client.Height;

            if (clientRatio > imageRatio)
            {
                var height = client.Height;
                var width = (int)Math.Round(height * imageRatio);
                return new Rectangle((client.Width - width) / 2, 0, width, height);
            }

            var fittedWidth = client.Width;
            var fittedHeight = (int)Math.Round(fittedWidth / imageRatio);
            return new Rectangle(0, (client.Height - fittedHeight) / 2, fittedWidth, fittedHeight);
        }

        private static Rectangle Normalize(Point first, Point second) =>
            new(
                Math.Min(first.X, second.X),
                Math.Min(first.Y, second.Y),
                Math.Abs(first.X - second.X),
                Math.Abs(first.Y - second.Y));
    }
}

internal sealed record RegionSelectionResult(bool Accepted, Rectangle? Region);
