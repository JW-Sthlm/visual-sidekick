namespace VisualSidekick.Server;

internal static class WindowPicker
{
    internal static WindowInfo? Pick(IReadOnlyList<WindowInfo> windows)
    {
        WindowInfo? selected = null;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var form = new Form
                {
                    Text = "Visual Sidekick: choose a window",
                    Width = 900,
                    Height = 620,
                    StartPosition = FormStartPosition.CenterScreen,
                    MinimizeBox = false,
                    MaximizeBox = false,
                    TopMost = true
                };

                var instructions = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 44,
                    Padding = new Padding(10),
                    Text = "Select the application window Visual Sidekick may inspect. Only this window is captured."
                };

                var list = new ListBox
                {
                    Dock = DockStyle.Fill,
                    Font = new Font("Segoe UI", 10),
                    HorizontalScrollbar = true
                };
                list.Items.AddRange(windows.Cast<object>().ToArray());
                if (list.Items.Count > 0)
                {
                    list.SelectedIndex = 0;
                }

                var buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    Height = 52,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(8)
                };

                var cancel = new Button { Text = "Cancel", Width = 100, DialogResult = DialogResult.Cancel };
                var observe = new Button { Text = "Observe", Width = 100, DialogResult = DialogResult.OK };
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(observe);

                form.Controls.Add(list);
                form.Controls.Add(instructions);
                form.Controls.Add(buttons);
                form.AcceptButton = observe;
                form.CancelButton = cancel;
                list.DoubleClick += (_, _) =>
                {
                    if (list.SelectedItem is not null)
                    {
                        form.DialogResult = DialogResult.OK;
                        form.Close();
                    }
                };

                if (form.ShowDialog() == DialogResult.OK)
                {
                    selected = list.SelectedItem as WindowInfo;
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
            throw new InvalidOperationException("The window picker failed.", failure);
        }

        return selected;
    }
}
