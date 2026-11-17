using System.Diagnostics;

namespace UnityTimeTracker.Core;

public enum TrackerState { NoUnity, Open, Active, Ide, Paused }

public sealed record TrackerStatus(TrackerState State, string? Project, string? Scene, long? ProjectId = null)
{
    public string Describe() => State switch
    {
        TrackerState.Paused => "Tracking paused",
        TrackerState.NoUnity => "Unity is not running",
        TrackerState.Active => Scene is null ? $"In Unity: {Project}" : $"In Unity: {Project}, scene {Scene}",
        TrackerState.Ide => $"In IDE: {Project}",
        _ => $"Unity open: {Project} (not focused or idle)",
    };
}

public sealed class Tracker : IDisposable
{
    static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(10);
    static readonly TimeSpan SleepGap = TimeSpan.FromSeconds(30);
    static readonly TimeSpan MinSpan = TimeSpan.FromSeconds(1);

    static readonly HashSet<string> IdeProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "rider64", "rider", "devenv", "Code", "Code - Insiders", "Cursor", "Windsurf", "VSCodium",
    };

    sealed class Editor(long sessionId, Project project)
    {
        public long SessionId { get; } = sessionId;
        public Project Project = project;
        public LiveSpan? Open;
        public LiveSpan? Active;
        public string? Scene;
    }

    readonly Database db;
    readonly AppSettings settings;
    readonly UnityScanner scanner = new();
    readonly object gate = new();
    readonly Dictionary<int, Editor> editors = [];
    readonly List<LiveSpan> closed = [];
    readonly Dictionary<int, string> processNames = [];
    List<Project> projects = [];
    DateTime projectsLoaded;
    LiveSpan? ide;
    DateTime lastTick, lastFlush;
    Timer? timer;
    int busy;
    bool disposed;

    public event Action<TrackerStatus>? StatusChanged;
    public event Action? ProjectsChanged;

    public TrackerStatus Status { get; private set; } = new(TrackerState.NoUnity, null, null);

    public Tracker(Database db, AppSettings settings)
    {
        this.db = db;
        this.settings = settings;
    }

    public void Start() => timer = new Timer(_ => OnTimer(), null, TimeSpan.Zero, Interval);

    void OnTimer()
    {
        if (Interlocked.Exchange(ref busy, 1) == 1) return;
        try
        {
            var now = DateTime.UtcNow;
            List<EditorInfo> found = settings.Paused ? [] : scanner.Scan();
            int fgPid = Native.ForegroundPid(out var fgWindow);
            var idle = Native.IdleTime();
            string? ideTitle = null;
            if (fgPid != 0 && found.All(e => e.Pid != fgPid) && IdeProcesses.Contains(ProcessName(fgPid)))
                ideTitle = Native.WindowText(fgWindow);

            lock (gate)
            {
                if (!disposed) Step(now, found, fgPid, ideTitle, idle);
            }
        }
        catch (Exception ex) { Log.Error(ex); }
        finally { Volatile.Write(ref busy, 0); }
    }

    void Step(DateTime now, List<EditorInfo> found, int fgPid, string? ideTitle, TimeSpan idle)
    {
        // A long gap between ticks means the PC slept; don't count that time.
        if (lastTick != default && now - lastTick > SleepGap) CloseAll(lastTick);
        lastTick = now;

        if (settings.Paused)
        {
            if (editors.Count > 0 || ide != null) CloseAll(now);
            Publish(new TrackerStatus(TrackerState.Paused, null, null));
            return;
        }

        bool present = idle < TimeSpan.FromMinutes(Math.Max(1, settings.IdleMinutes));
        var lastInput = now - idle;
        var stopAt = present ? now : lastInput;
        Editor? focused = null, first = null;
        var seen = new HashSet<int>();

        foreach (var info in found)
        {
            var ed = Attach(info, now);
            if (ed == null) continue;
            seen.Add(info.Pid);
            first ??= ed;
            if (info.Scene != null) ed.Scene = info.Scene;

            ed.Open ??= NewSpan(ed, SpanKind.Open, null, now);
            ed.Open.EndUtc = now;

            if (present && info.Pid == fgPid)
            {
                focused = ed;
                if (ed.Active != null && ed.Active.Scene != ed.Scene) Close(ref ed.Active, now);
                ed.Active ??= NewSpan(ed, SpanKind.Editor, ed.Scene, now);
                ed.Active.EndUtc = now;
            }
            else
            {
                Close(ref ed.Active, stopAt);
            }
        }

        foreach (var pid in editors.Keys.Where(p => !seen.Contains(p)).ToList()) Detach(pid, now);

        var ideProject = present && settings.TrackIde && ideTitle != null ? MatchIde(ideTitle, now) : null;
        if (ide != null && ide.ProjectId != ideProject?.Id) Close(ref ide, stopAt);
        if (ideProject != null)
        {
            ide ??= new LiveSpan { ProjectId = ideProject.Id, Kind = SpanKind.Ide, StartUtc = now, EndUtc = now };
            ide.EndUtc = now;
        }

        if (now - lastFlush >= FlushEvery) FlushCore(now);

        Publish(focused != null ? new TrackerStatus(TrackerState.Active, focused.Project.Title, focused.Scene, focused.Project.Id)
            : ideProject != null ? new TrackerStatus(TrackerState.Ide, ideProject.Title, null, ideProject.Id)
            : first != null ? new TrackerStatus(TrackerState.Open, first.Project.Title, first.Scene, first.Project.Id)
            : new TrackerStatus(TrackerState.NoUnity, null, null));
    }

    Editor? Attach(EditorInfo info, DateTime now)
    {
        if (editors.TryGetValue(info.Pid, out var ed)) return ed;

        var folder = info.ProjectPath;
        var name = folder != null ? Path.GetFileName(folder) : info.TitleProject;
        if (string.IsNullOrEmpty(name)) return null;

        var project = folder != null
            ? db.GetOrCreateProject(folder, name)
            : db.FindProjectByName(name) ?? db.GetOrCreateProject(ProjectPaths.TitlePrefix + name, name);
        db.MarkOpened(project.Id, now, info.Version);

        ed = new Editor(db.InsertSession(project.Id, info.Pid, info.Version, now), project) { Scene = info.Scene };
        editors[info.Pid] = ed;
        projectsLoaded = default;
        ProjectsChanged?.Invoke();
        return ed;
    }

    static LiveSpan NewSpan(Editor ed, SpanKind kind, string? scene, DateTime now) => new()
    {
        ProjectId = ed.Project.Id,
        SessionId = ed.SessionId,
        Kind = kind,
        Scene = scene,
        StartUtc = now,
        EndUtc = now,
    };

    void Close(ref LiveSpan? span, DateTime end)
    {
        if (span == null) return;
        span.EndUtc = end > span.StartUtc ? end : span.StartUtc;
        if (span.Id != 0 || span.EndUtc - span.StartUtc >= MinSpan) closed.Add(span);
        span = null;
    }

    void Detach(int pid, DateTime end)
    {
        var ed = editors[pid];
        Close(ref ed.Active, end);
        Close(ref ed.Open, end);
        editors.Remove(pid);
        db.UpdateSessionEnds([ed.SessionId], end);
    }

    void CloseAll(DateTime end)
    {
        foreach (var pid in editors.Keys.ToList()) Detach(pid, end);
        Close(ref ide, end);
        FlushCore(end);
    }

    void FlushCore(DateTime now)
    {
        var batch = new List<LiveSpan>(closed);
        foreach (var ed in editors.Values)
        {
            if (Worth(ed.Open)) batch.Add(ed.Open!);
            if (Worth(ed.Active)) batch.Add(ed.Active!);
        }
        if (Worth(ide)) batch.Add(ide!);

        db.SaveSpans(batch);
        if (editors.Count > 0) db.UpdateSessionEnds(editors.Values.Select(e => e.SessionId), now);
        closed.Clear();
        lastFlush = now;

        static bool Worth(LiveSpan? s) => s != null && (s.Id != 0 || s.EndUtc - s.StartUtc >= MinSpan);
    }

    public void Flush()
    {
        lock (gate)
        {
            if (disposed) return;
            try { FlushCore(DateTime.UtcNow); }
            catch (Exception ex) { Log.Error(ex); }
        }
    }

    // Drops live state for a project whose data was just deleted.
    public void Forget(long projectId)
    {
        lock (gate)
        {
            foreach (var pid in editors.Where(e => e.Value.Project.Id == projectId).Select(e => e.Key).ToList()) editors.Remove(pid);
            if (ide?.ProjectId == projectId) ide = null;
            closed.RemoveAll(s => s.ProjectId == projectId);
            projectsLoaded = default;
        }
    }

    public void ReloadProjects()
    {
        lock (gate) projectsLoaded = default;
    }

    // Live work spans of a project restart on the next tick, e.g. after their rows were deleted.
    public void RestartWork(long projectId)
    {
        lock (gate)
        {
            foreach (var ed in editors.Values.Where(e => e.Project.Id == projectId)) ed.Active = null;
            if (ide?.ProjectId == projectId) ide = null;
            closed.RemoveAll(s => s.ProjectId == projectId && s.Kind != SpanKind.Open);
        }
    }

    Project? MatchIde(string title, DateTime now)
    {
        if (now - projectsLoaded > TimeSpan.FromSeconds(30))
        {
            projects = db.LoadProjects();
            projectsLoaded = now;
            var byId = projects.ToDictionary(p => p.Id);
            foreach (var ed in editors.Values)
                if (byId.TryGetValue(ed.Project.Id, out var fresh)) ed.Project = fresh;
        }

        Project? best = null;
        int bestScore = -1;
        foreach (var p in projects)
        {
            foreach (var name in new[] { p.Name, p.DisplayName })
            {
                if (string.IsNullOrWhiteSpace(name) || !ContainsWord(title, name.Trim())) continue;
                int score = name.Length + (editors.Values.Any(e => e.Project.Id == p.Id) ? 1000 : 0);
                if (score > bestScore)
                {
                    best = p;
                    bestScore = score;
                }
            }
        }
        return best;
    }

    static bool ContainsWord(string text, string word)
    {
        int i = 0;
        while ((i = text.IndexOf(word, i, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int end = i + word.Length;
            if ((i == 0 || !char.IsLetterOrDigit(text[i - 1])) && (end == text.Length || !char.IsLetterOrDigit(text[end])))
                return true;
            i++;
        }
        return false;
    }

    string ProcessName(int pid)
    {
        if (processNames.TryGetValue(pid, out var name)) return name;
        try
        {
            using var p = Process.GetProcessById(pid);
            name = p.ProcessName;
        }
        catch (ArgumentException) { name = ""; }
        catch (InvalidOperationException) { name = ""; }
        if (processNames.Count > 512) processNames.Clear();
        processNames[pid] = name;
        return name;
    }

    void Publish(TrackerStatus status)
    {
        if (status == Status) return;
        Status = status;
        StatusChanged?.Invoke(status);
    }

    public void Dispose()
    {
        timer?.Dispose();
        lock (gate)
        {
            if (disposed) return;
            try { CloseAll(DateTime.UtcNow); }
            catch (Exception ex) { Log.Error(ex); }
            disposed = true;
        }
    }
}
