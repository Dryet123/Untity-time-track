using System.Windows.Media;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Controls;

public sealed class ShareRow
{
    public long ProjectId { get; init; }
    public Project? Project { get; init; }
    public string Name { get; init; } = "";
    public string? Sub { get; init; }
    public double Fraction { get; init; }
    public Brush? Fill { get; init; }
    public string Time { get; init; } = "";
    public string Percent { get; init; } = "";
}

public sealed class SessionRow
{
    public required WorkSession Session { get; init; }
    public Project? Project { get; init; }
    public string Name => Project?.Title ?? "Unknown";
    public DateTime Start => Session.Start;
    public DateTime End => Session.End;
    public TimeSpan Work => Session.Work;
    public TimeSpan Editor => Session.Editor;
    public TimeSpan Ide => Session.Ide;
    public string Date => Fmt.Day(Clock.DayOf(Session.Start));
    public string StartText => Fmt.Time(Session.Start);
    public string EndText => Fmt.Time(Session.End);
    public string Range => $"{StartText} - {EndText}";
    public string WorkText => Fmt.Dur(Session.Work);
    public string EditorText => Fmt.Dur(Session.Editor);
    public string IdeText => Session.Ide > TimeSpan.Zero ? Fmt.Dur(Session.Ide) : "";
    public string Scenes => string.Join(", ", Session.Scenes.OrderByDescending(kv => kv.Value).Select(kv => kv.Key));
    public string ScenesSuffix => Session.Scenes.Count > 0 ? "   " + Scenes : "";

    public static SessionRow From(WorkSession s, DataSnapshot snap) => new() { Session = s, Project = snap.Get(s.ProjectId) };
}
