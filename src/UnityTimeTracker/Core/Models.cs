namespace UnityTimeTracker.Core;

public enum SpanKind { Open = 0, Editor = 1, Ide = 2 }

public sealed class Project
{
    public long Id { get; init; }
    public string Folder { get; init; } = "";
    public string Name { get; init; } = "";
    public string? DisplayName { get; set; }
    public string Color { get; set; } = ProjectColors.All[0];
    public string? Icon { get; set; }
    public bool Hidden { get; set; }
    public string? UnityVersion { get; init; }
    public string? Pipeline { get; init; }
    public string? BuildTarget { get; init; }
    public string? Repo { get; init; }
    public DateTime Created { get; init; }
    public DateTime? LastOpened { get; init; }

    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName.Trim();
    public bool HasFolder => !Folder.StartsWith(ProjectPaths.TitlePrefix, StringComparison.Ordinal);
}

public sealed record SpanRec(long Id, long ProjectId, long SessionId, SpanKind Kind, string? Scene, DateTime Start, DateTime End)
{
    public TimeSpan Duration => End - Start;
}

public sealed record SessionRec(long Id, long ProjectId, int Pid, string? Version, DateTime Start, DateTime End);

public sealed class LiveSpan
{
    public long Id { get; set; }
    public long ProjectId { get; init; }
    public long SessionId { get; init; }
    public SpanKind Kind { get; init; }
    public string? Scene { get; init; }
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; set; }
}

public sealed record HubProject(string Folder, string Name, string? Version, string? Pipeline, string? BuildTarget, string? Repo);

public static class ProjectColors
{
    public static readonly string[] All =
    [
        "#3A79BB", "#5AA14B", "#D08A3A", "#C2504B", "#8A63B8", "#3FA6A0",
        "#C9B13F", "#C2588F", "#7F9A45", "#5C9FE0", "#B07A55", "#8585B8",
    ];
}

public static class ProjectPaths
{
    // Projects whose folder is unknown (no -projectPath) are keyed by window title.
    public const string TitlePrefix = "title:";

    public static string Normalize(string path)
    {
        path = path.Trim().Trim('"');
        try { path = Path.GetFullPath(path); } catch { }
        return path.TrimEnd('\\', '/');
    }
}
