using System.Windows;
using UnityTimeTracker.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace UnityTimeTracker.UI;

public sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon icon;
    readonly Drawing.Icon active = Load("tray-active.ico");
    readonly Drawing.Icon idle = Load("tray-idle.ico");
    readonly Drawing.Icon paused = Load("tray-paused.ico");
    readonly Forms.ToolStripMenuItem pauseItem;

    public event Action? OpenRequested;
    public event Action? PauseToggled;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new Forms.ToolStripProfessionalRenderer(new DarkMenuColors()) { RoundedEdges = false },
            ShowImageMargin = false,
            Font = new Drawing.Font("Arial", 9f),
        };
        menu.Items.Add(Item("Open", () => OpenRequested?.Invoke()));
        pauseItem = Item("Pause tracking", () => PauseToggled?.Invoke());
        menu.Items.Add(pauseItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Item("Exit", () => ExitRequested?.Invoke()));

        icon = new Forms.NotifyIcon { Icon = idle, Text = "Unity Time Tracker", ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke();
        };
    }

    static Forms.ToolStripMenuItem Item(string text, Action onClick)
    {
        var item = new Forms.ToolStripMenuItem(text) { ForeColor = Drawing.Color.FromArgb(220, 220, 220), Padding = new Forms.Padding(4, 3, 4, 3) };
        item.Click += (_, _) => onClick();
        return item;
    }

    static Drawing.Icon Load(string name)
    {
        using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}")).Stream;
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Update(TrackerStatus status, TimeSpan today, bool isPaused)
    {
        icon.Icon = isPaused ? paused : status.State is TrackerState.Active or TrackerState.Ide ? active : idle;
        var text = $"Unity Time Tracker\n{status.Describe()}\nToday {Fmt.Dur(today)}";
        icon.Text = text.Length > 127 ? text[..127] : text;
        pauseItem.Text = isPaused ? "Resume tracking" : "Pause tracking";
    }

    public void Notify(string title, string text) => icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.None);

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }

    sealed class DarkMenuColors : Forms.ProfessionalColorTable
    {
        static readonly Drawing.Color Back = Drawing.Color.FromArgb(56, 56, 56);
        static readonly Drawing.Color Select = Drawing.Color.FromArgb(44, 93, 135);
        static readonly Drawing.Color Border = Drawing.Color.FromArgb(26, 26, 26);

        public override Drawing.Color ToolStripDropDownBackground => Back;
        public override Drawing.Color ImageMarginGradientBegin => Back;
        public override Drawing.Color ImageMarginGradientMiddle => Back;
        public override Drawing.Color ImageMarginGradientEnd => Back;
        public override Drawing.Color MenuBorder => Border;
        public override Drawing.Color MenuItemBorder => Select;
        public override Drawing.Color MenuItemSelected => Select;
        public override Drawing.Color MenuItemSelectedGradientBegin => Select;
        public override Drawing.Color MenuItemSelectedGradientEnd => Select;
        public override Drawing.Color SeparatorDark => Border;
        public override Drawing.Color SeparatorLight => Drawing.Color.FromArgb(70, 70, 70);
    }
}
