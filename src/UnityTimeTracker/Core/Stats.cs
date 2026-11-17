namespace UnityTimeTracker.Core;

// Logical days: with DayStartHour = 4, 01:30 on Tuesday still belongs to Monday.
public static class Clock
{
    public static int Offset { get; set; }

    public static DateOnly DayOf(DateTime t) => DateOnly.FromDateTime(t.AddHours(-Offset));
    public static DateTime Start(DateOnly d) => d.ToDateTime(TimeOnly.MinValue).AddHours(Offset);
    public static DateOnly Today => DayOf(DateTime.Now);
    public static int Weekday(DateOnly d) => ((int)d.DayOfWeek + 6) % 7;
    public static DateOnly Monday(DateOnly d) => d.AddDays(-Weekday(d));
    public static TimeRange Range(DateOnly from, DateOnly to) => new(Start(from), Start(to));
}

public readonly record struct TimeRange(DateTime From, DateTime To)
{
    public bool Contains(DateTime t) => t >= From && t < To;

    public TimeSpan Overlap(DateTime start, DateTime end)
    {
        var a = start > From ? start : From;
        var b = end < To ? end : To;
        return b > a ? b - a : TimeSpan.Zero;
    }
}

public enum PeriodKind { Day, Week, Month, Year, All }

public readonly record struct Period(PeriodKind Kind, DateOnly Anchor)
{
    public (DateOnly From, DateOnly To) Days(DateOnly firstData)
    {
        var today = Clock.Today;
        switch (Kind)
        {
            case PeriodKind.Day: return (Anchor, Anchor.AddDays(1));
            case PeriodKind.Week: return (Clock.Monday(Anchor), Clock.Monday(Anchor).AddDays(7));
            case PeriodKind.Month:
                var m = new DateOnly(Anchor.Year, Anchor.Month, 1);
                return (m, m.AddMonths(1));
            case PeriodKind.Year: return (new DateOnly(Anchor.Year, 1, 1), new DateOnly(Anchor.Year + 1, 1, 1));
            default: return (firstData < today ? firstData : today, today.AddDays(1));
        }
    }

    public Period Shift(int n) => Kind switch
    {
        PeriodKind.Day => this with { Anchor = Anchor.AddDays(n) },
        PeriodKind.Week => this with { Anchor = Anchor.AddDays(7 * n) },
        PeriodKind.Month => this with { Anchor = Anchor.AddMonths(n) },
        PeriodKind.Year => this with { Anchor = Anchor.AddYears(n) },
        _ => this,
    };

    public string Label
    {
        get
        {
            switch (Kind)
            {
                case PeriodKind.Day: return Fmt.Day(Anchor);
                case PeriodKind.Week:
                    var a = Clock.Monday(Anchor);
                    var b = a.AddDays(6);
                    if (a.Month == b.Month) return $"{a.Day} - {Fmt.Format(b, "d MMM yyyy")}";
                    if (a.Year == b.Year) return $"{Fmt.Format(a, "d MMM")} - {Fmt.Format(b, "d MMM yyyy")}";
                    return $"{Fmt.Format(a, "d MMM yyyy")} - {Fmt.Format(b, "d MMM yyyy")}";
                case PeriodKind.Month: return Fmt.Month(Anchor);
                case PeriodKind.Year: return Anchor.Year.ToString();
                default: return "All time";
            }
        }
    }
}

public sealed class Totals
{
    public TimeSpan Editor { get; private set; }
    public TimeSpan Ide { get; private set; }
    public TimeSpan Open { get; private set; }
    public TimeSpan Work => Editor + Ide;

    public void Add(SpanKind kind, TimeSpan t)
    {
        switch (kind)
        {
            case SpanKind.Editor: Editor += t; break;
            case SpanKind.Ide: Ide += t; break;
            default: Open += t; break;
        }
    }
}

public sealed class WorkSession
{
    public required long ProjectId { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; set; }
    public TimeSpan Editor { get; set; }
    public TimeSpan Ide { get; set; }
    public TimeSpan Work => Editor + Ide;
    public bool Manual { get; set; }
    public Dictionary<string, TimeSpan> Scenes { get; } = [];
}

public sealed class DataSnapshot
{
    readonly Dictionary<long, List<WorkSession>> sessionCache = [];

    public required IReadOnlyList<Project> Projects { get; init; }
    public required IReadOnlyDictionary<long, Project> ById { get; init; }
    public required IReadOnlyList<SpanRec> Spans { get; init; }
    public required IReadOnlyList<SessionRec> Sessions { get; init; }
    public required IReadOnlyDictionary<DateOnly, string> Notes { get; init; }

    public static DataSnapshot Load(Database db)
    {
        var projects = db.LoadProjects();
        return new DataSnapshot
        {
            Projects = projects,
            ById = projects.ToDictionary(p => p.Id),
            Spans = db.LoadSpans(),
            Sessions = db.LoadSessions(),
            Notes = db.LoadNotes(),
        };
    }

    // Cheap fingerprint used to skip redrawing when nothing changed.
    public string Signature =>
        $"{Spans.Count}|{(Spans.Count > 0 ? Spans.Max(s => s.End).Ticks : 0)}|{string.Join(",", Projects.Select(p => $"{p.Id}{p.Title}{p.Color}{p.Icon}{p.Hidden}"))}|{Notes.Count}|{Notes.Values.Sum(n => n.Length)}";

    public DateOnly FirstDay => Spans.Count > 0 ? Clock.DayOf(Spans[0].Start) : Clock.Today;

    public Project? Get(long id) => ById.GetValueOrDefault(id);

    bool Visible(long projectId) => ById.TryGetValue(projectId, out var p) && !p.Hidden;

    // null = every project that isn't hidden.
    public IEnumerable<SpanRec> Of(long? project) =>
        project is long id ? Spans.Where(s => s.ProjectId == id) : Spans.Where(s => Visible(s.ProjectId));

    public IEnumerable<SpanRec> Work(long? project) => Of(project).Where(s => s.Kind != SpanKind.Open);

    public IEnumerable<SessionRec> EditorSessions(long? project) =>
        project is long id ? Sessions.Where(s => s.ProjectId == id) : Sessions.Where(s => Visible(s.ProjectId));

    public List<WorkSession> WorkSessions(long? project)
    {
        long key = project ?? -1;
        if (!sessionCache.TryGetValue(key, out var list))
            sessionCache[key] = list = Stats.BuildSessions(Work(project));
        return list;
    }
}

public static class Stats
{
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(10);

    public static Totals Sum(IEnumerable<SpanRec> spans, TimeRange r)
    {
        var t = new Totals();
        foreach (var s in spans)
        {
            var o = r.Overlap(s.Start, s.End);
            if (o > TimeSpan.Zero) t.Add(s.Kind, o);
        }
        return t;
    }

    public static Dictionary<long, Totals> PerProject(IEnumerable<SpanRec> spans, TimeRange r)
    {
        var map = new Dictionary<long, Totals>();
        foreach (var s in spans)
        {
            var o = r.Overlap(s.Start, s.End);
            if (o <= TimeSpan.Zero) continue;
            if (!map.TryGetValue(s.ProjectId, out var t)) map[s.ProjectId] = t = new Totals();
            t.Add(s.Kind, o);
        }
        return map;
    }

    // Splits spans across consecutive [edges[i], edges[i+1]) buckets, per project.
    public static Dictionary<long, TimeSpan>[] Buckets(IEnumerable<SpanRec> spans, IReadOnlyList<DateTime> edges)
    {
        int n = Math.Max(0, edges.Count - 1);
        var result = new Dictionary<long, TimeSpan>[n];
        for (int i = 0; i < n; i++) result[i] = [];
        if (n == 0) return result;

        foreach (var span in spans)
        {
            var s = span.Start < edges[0] ? edges[0] : span.Start;
            var e = span.End > edges[n] ? edges[n] : span.End;
            if (s >= e) continue;
            int i = FindBucket(edges, s);
            while (s < e && i < n)
            {
                var b = e < edges[i + 1] ? e : edges[i + 1];
                if (b > s) result[i][span.ProjectId] = result[i].GetValueOrDefault(span.ProjectId) + (b - s);
                s = b;
                i++;
            }
        }
        return result;
    }

    static int FindBucket(IReadOnlyList<DateTime> edges, DateTime t)
    {
        int lo = 0, hi = edges.Count - 2;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (edges[mid] <= t) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    public static Dictionary<DateOnly, Dictionary<long, TimeSpan>> PerDay(IEnumerable<SpanRec> spans, DateOnly from, DateOnly to)
    {
        var edges = new List<DateTime>();
        for (var d = from; d <= to; d = d.AddDays(1)) edges.Add(Clock.Start(d));
        var buckets = Buckets(spans, edges);
        var map = new Dictionary<DateOnly, Dictionary<long, TimeSpan>>();
        for (int i = 0; i < buckets.Length; i++)
            if (buckets[i].Count > 0) map[from.AddDays(i)] = buckets[i];
        return map;
    }

    public static TimeSpan Total(Dictionary<long, TimeSpan>? perProject) =>
        perProject == null ? TimeSpan.Zero : perProject.Values.Aggregate(TimeSpan.Zero, (a, b) => a + b);

    public static double[] HourOfDay(IEnumerable<SpanRec> spans, TimeRange r)
    {
        var hours = new double[24];
        foreach (var span in spans)
        {
            var s = span.Start < r.From ? r.From : span.Start;
            var e = span.End > r.To ? r.To : span.End;
            while (s < e)
            {
                var next = new DateTime(s.Year, s.Month, s.Day, s.Hour, 0, 0).AddHours(1);
                var b = e < next ? e : next;
                hours[s.Hour] += (b - s).TotalHours;
                s = b;
            }
        }
        return hours;
    }

    // Work spans of one project closer than SessionGap merge into one session.
    public static List<WorkSession> BuildSessions(IEnumerable<SpanRec> work)
    {
        var current = new Dictionary<long, WorkSession>();
        var result = new List<WorkSession>();
        foreach (var s in work)
        {
            if (!current.TryGetValue(s.ProjectId, out var ws) || s.Start - ws.End > SessionGap)
            {
                ws = new WorkSession { ProjectId = s.ProjectId, Start = s.Start, End = s.End };
                current[s.ProjectId] = ws;
                result.Add(ws);
            }
            else if (s.End > ws.End)
            {
                ws.End = s.End;
            }
            if (s.Manual) ws.Manual = true;

            if (s.Kind == SpanKind.Ide)
            {
                ws.Ide += s.Duration;
            }
            else
            {
                ws.Editor += s.Duration;
                if (s.Scene != null) ws.Scenes[s.Scene] = ws.Scenes.GetValueOrDefault(s.Scene) + s.Duration;
            }
        }
        return result;
    }

    public static (int Current, int Best) Streaks(IEnumerable<SpanRec> work)
    {
        var perDay = new Dictionary<DateOnly, TimeSpan>();
        foreach (var s in work)
            foreach (var (day, t) in SplitDays(s.Start, s.End))
                perDay[day] = perDay.GetValueOrDefault(day) + t;
        var days = perDay.Where(kv => kv.Value >= TimeSpan.FromMinutes(1)).Select(kv => kv.Key).ToHashSet();

        int best = 0;
        foreach (var d in days)
        {
            if (days.Contains(d.AddDays(-1))) continue;
            int n = 1;
            while (days.Contains(d.AddDays(n))) n++;
            best = Math.Max(best, n);
        }

        var cursor = days.Contains(Clock.Today) ? Clock.Today : Clock.Today.AddDays(-1);
        int current = 0;
        while (days.Contains(cursor))
        {
            current++;
            cursor = cursor.AddDays(-1);
        }
        return (current, best);
    }

    public static IEnumerable<(DateOnly Day, TimeSpan Time)> SplitDays(DateTime start, DateTime end)
    {
        while (start < end)
        {
            var day = Clock.DayOf(start);
            var next = Clock.Start(day.AddDays(1));
            var b = end < next ? end : next;
            yield return (day, b - start);
            start = b;
        }
    }

    // Median first start and last end per worked day, as offsets from midnight.
    public static (TimeSpan Start, TimeSpan End)? UsualHours(IEnumerable<SpanRec> work, TimeRange r)
    {
        var first = new Dictionary<DateOnly, DateTime>();
        var last = new Dictionary<DateOnly, DateTime>();
        foreach (var s in work)
        {
            if (s.End <= r.From || s.Start >= r.To) continue;
            var a = s.Start < r.From ? r.From : s.Start;
            var b = s.End > r.To ? r.To : s.End;
            var da = Clock.DayOf(a);
            if (!first.TryGetValue(da, out var f) || a < f) first[da] = a;
            var db = Clock.DayOf(b.AddTicks(-1));
            if (!last.TryGetValue(db, out var l) || b > l) last[db] = b;
        }
        if (first.Count == 0) return null;

        var offset = TimeSpan.FromHours(Clock.Offset);
        var starts = first.Select(kv => kv.Value - Clock.Start(kv.Key)).Order().ToList();
        var ends = last.Select(kv => kv.Value - Clock.Start(kv.Key)).Order().ToList();
        return (starts[starts.Count / 2] + offset, ends[ends.Count / 2] + offset);
    }

    public static List<(long ProjectId, string Scene, TimeSpan Time)> Scenes(IEnumerable<SpanRec> spans, TimeRange r)
    {
        var map = new Dictionary<(long, string), TimeSpan>();
        foreach (var s in spans)
        {
            if (s.Kind != SpanKind.Editor || s.Scene == null) continue;
            var o = r.Overlap(s.Start, s.End);
            if (o > TimeSpan.Zero) map[(s.ProjectId, s.Scene)] = map.GetValueOrDefault((s.ProjectId, s.Scene)) + o;
        }
        return map.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Value)).ToList();
    }

    public sealed record FocusStats(int Stretches, TimeSpan Average, TimeSpan Longest, double PerHour);

    // A focus stretch is uninterrupted Unity time: editor spans less than 30 s apart (scene switches) merge into one.
    public static FocusStats? Focus(IEnumerable<SpanRec> spans, TimeRange r)
    {
        var gap = TimeSpan.FromSeconds(30);
        var stretches = new List<TimeSpan>();
        DateTime start = default, end = default;
        foreach (var s in spans.Where(s => s.Kind == SpanKind.Editor && !s.Manual && s.End > r.From && s.Start < r.To).OrderBy(s => s.Start))
        {
            var a = s.Start < r.From ? r.From : s.Start;
            var b = s.End > r.To ? r.To : s.End;
            if (stretches.Count > 0 && a - end <= gap)
            {
                if (b > end) end = b;
                stretches[^1] = end - start;
            }
            else
            {
                start = a;
                end = b;
                stretches.Add(b - a);
            }
        }
        var total = stretches.Aggregate(TimeSpan.Zero, (x, y) => x + y);
        if (stretches.Count == 0 || total < TimeSpan.FromMinutes(5)) return null;
        return new FocusStats(stretches.Count, total / stretches.Count, stretches.Max(), stretches.Count / total.TotalHours);
    }

    // Hours of work per [weekday, clock hour].
    public static double[,] Rhythm(IEnumerable<SpanRec> work, TimeRange r)
    {
        var grid = new double[7, 24];
        foreach (var span in work)
        {
            var s = span.Start < r.From ? r.From : span.Start;
            var e = span.End > r.To ? r.To : span.End;
            while (s < e)
            {
                var next = new DateTime(s.Year, s.Month, s.Day, s.Hour, 0, 0).AddHours(1);
                var b = e < next ? e : next;
                grid[Clock.Weekday(DateOnly.FromDateTime(s)), s.Hour] += (b - s).TotalHours;
                s = b;
            }
        }
        return grid;
    }

    public static readonly (string Label, TimeSpan Max)[] LengthBuckets =
    [
        ("<15m", TimeSpan.FromMinutes(15)), ("15-30m", TimeSpan.FromMinutes(30)), ("30m-1h", TimeSpan.FromHours(1)),
        ("1-2h", TimeSpan.FromHours(2)), ("2-4h", TimeSpan.FromHours(4)), ("4h+", TimeSpan.MaxValue),
    ];

    public static int[] SessionLengths(IEnumerable<WorkSession> sessions)
    {
        var counts = new int[LengthBuckets.Length];
        foreach (var s in sessions)
            counts[Array.FindIndex(LengthBuckets, b => s.Work < b.Max)]++;
        return counts;
    }
}
