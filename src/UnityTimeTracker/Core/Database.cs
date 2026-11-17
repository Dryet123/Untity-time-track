using Microsoft.Data.Sqlite;

namespace UnityTimeTracker.Core;

public sealed class Database
{
    const string ProjectColumns =
        "id, path, name, display_name, color, icon, hidden, unity_version, pipeline, build_target, repo, created, last_opened";

    readonly string connection;

    public Database(string file)
    {
        connection = new SqliteConnectionStringBuilder { DataSource = file, Pooling = true }.ToString();
        using var c = Open();
        Exec(c, "PRAGMA journal_mode=WAL;");
        Exec(c, """
            CREATE TABLE IF NOT EXISTS projects(
                id INTEGER PRIMARY KEY,
                path TEXT NOT NULL UNIQUE COLLATE NOCASE,
                name TEXT NOT NULL,
                display_name TEXT,
                color TEXT NOT NULL,
                icon TEXT,
                hidden INTEGER NOT NULL DEFAULT 0,
                unity_version TEXT,
                pipeline TEXT,
                build_target TEXT,
                repo TEXT,
                created INTEGER NOT NULL,
                last_opened INTEGER);
            CREATE TABLE IF NOT EXISTS sessions(
                id INTEGER PRIMARY KEY,
                project_id INTEGER NOT NULL,
                pid INTEGER NOT NULL,
                unity_version TEXT,
                start_ms INTEGER NOT NULL,
                end_ms INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS spans(
                id INTEGER PRIMARY KEY,
                project_id INTEGER NOT NULL,
                session_id INTEGER NOT NULL DEFAULT 0,
                kind INTEGER NOT NULL,
                scene TEXT,
                start_ms INTEGER NOT NULL,
                end_ms INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_spans_project ON spans(project_id);
            CREATE INDEX IF NOT EXISTS ix_spans_end ON spans(end_ms);
            """);
        Migrate(c);
    }

    static void Migrate(SqliteConnection c)
    {
        using var get = Cmd(c, "PRAGMA user_version");
        var version = (long)get.ExecuteScalar()!;
        if (version < 1)
        {
            Exec(c, """
                ALTER TABLE spans ADD COLUMN manual INTEGER NOT NULL DEFAULT 0;
                CREATE TABLE IF NOT EXISTS notes(day TEXT PRIMARY KEY, text TEXT NOT NULL);
                PRAGMA user_version = 1;
                """);
        }
    }

    SqliteConnection Open()
    {
        var c = new SqliteConnection(connection);
        c.Open();
        return c;
    }

    static SqliteCommand Cmd(SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    static void Exec(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = Cmd(c, sql, args);
        cmd.ExecuteNonQuery();
    }

    public static long Ms(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    static DateTime Local(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

    static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    static Project ReadProject(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Folder = r.GetString(1),
        Name = r.GetString(2),
        DisplayName = Str(r, 3),
        Color = r.GetString(4),
        Icon = Str(r, 5),
        Hidden = r.GetInt64(6) != 0,
        UnityVersion = Str(r, 7),
        Pipeline = Str(r, 8),
        BuildTarget = Str(r, 9),
        Repo = Str(r, 10),
        Created = Local(r.GetInt64(11)),
        LastOpened = r.IsDBNull(12) ? null : Local(r.GetInt64(12)),
    };

    static List<Project> QueryProjects(SqliteConnection c, string where, params (string, object?)[] args)
    {
        using var cmd = Cmd(c, $"SELECT {ProjectColumns} FROM projects {where}", args);
        using var r = cmd.ExecuteReader();
        var list = new List<Project>();
        while (r.Read()) list.Add(ReadProject(r));
        return list;
    }

    public List<Project> LoadProjects()
    {
        using var c = Open();
        return QueryProjects(c, "ORDER BY id");
    }

    public Project? FindProjectByName(string name)
    {
        using var c = Open();
        return QueryProjects(c, "WHERE name = $n COLLATE NOCASE ORDER BY path LIKE 'title:%', last_opened DESC LIMIT 1", ("$n", name))
            .FirstOrDefault();
    }

    public Project GetOrCreateProject(string folder, string name)
    {
        using var c = Open();
        var existing = QueryProjects(c, "WHERE path = $p", ("$p", folder)).FirstOrDefault();
        if (existing != null) return existing;
        Exec(c, "INSERT INTO projects(path, name, color, created) VALUES($p, $n, $c, $t) ON CONFLICT(path) DO NOTHING",
            ("$p", folder), ("$n", name), ("$c", NextColor(c)), ("$t", Ms(DateTime.UtcNow)));
        return QueryProjects(c, "WHERE path = $p", ("$p", folder)).First();
    }

    static string NextColor(SqliteConnection c)
    {
        using var cmd = Cmd(c, "SELECT COUNT(*) FROM projects");
        var count = (long)cmd.ExecuteScalar()!;
        return ProjectColors.All[(int)(count % ProjectColors.All.Length)];
    }

    public void UpsertHubProject(HubProject h)
    {
        using var c = Open();
        Exec(c, """
            INSERT INTO projects(path, name, color, unity_version, pipeline, build_target, repo, created)
            VALUES($p, $n, $c, $v, $pl, $b, $r, $t)
            ON CONFLICT(path) DO UPDATE SET
                unity_version = COALESCE(excluded.unity_version, unity_version),
                pipeline = COALESCE(excluded.pipeline, pipeline),
                build_target = COALESCE(excluded.build_target, build_target),
                repo = COALESCE(excluded.repo, repo)
            """,
            ("$p", h.Folder), ("$n", h.Name), ("$c", NextColor(c)), ("$v", h.Version), ("$pl", h.Pipeline),
            ("$b", h.BuildTarget), ("$r", h.Repo), ("$t", Ms(DateTime.UtcNow)));
    }

    public void MarkOpened(long id, DateTime utc, string? version)
    {
        using var c = Open();
        Exec(c, "UPDATE projects SET last_opened = $t, unity_version = COALESCE($v, unity_version) WHERE id = $id",
            ("$t", Ms(utc)), ("$v", version), ("$id", id));
    }

    public void UpdateProject(Project p)
    {
        using var c = Open();
        Exec(c, "UPDATE projects SET display_name = $d, color = $c, icon = $i, hidden = $h WHERE id = $id",
            ("$d", p.DisplayName), ("$c", p.Color), ("$i", p.Icon), ("$h", p.Hidden ? 1 : 0), ("$id", p.Id));
    }

    public void DeleteProject(long id)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var sql in new[] { "DELETE FROM spans WHERE project_id = $id", "DELETE FROM sessions WHERE project_id = $id", "DELETE FROM projects WHERE id = $id" })
        {
            using var cmd = Cmd(c, sql, ("$id", id));
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public long InsertSession(long projectId, int pid, string? version, DateTime startUtc)
    {
        using var c = Open();
        using var cmd = Cmd(c, "INSERT INTO sessions(project_id, pid, unity_version, start_ms, end_ms) VALUES($p, $pid, $v, $t, $t) RETURNING id",
            ("$p", projectId), ("$pid", pid), ("$v", version), ("$t", Ms(startUtc)));
        return (long)cmd.ExecuteScalar()!;
    }

    public void UpdateSessionEnds(IEnumerable<long> ids, DateTime endUtc)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var cmd = Cmd(c, "UPDATE sessions SET end_ms = $t WHERE id = $id", ("$t", Ms(endUtc)), ("$id", 0L));
        cmd.Transaction = tx;
        foreach (var id in ids)
        {
            cmd.Parameters["$id"].Value = id;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void SaveSpans(IReadOnlyCollection<LiveSpan> spans)
    {
        if (spans.Count == 0) return;
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var insert = Cmd(c, "INSERT INTO spans(project_id, session_id, kind, scene, start_ms, end_ms) VALUES($p, $s, $k, $sc, $a, $b) RETURNING id",
            ("$p", 0L), ("$s", 0L), ("$k", 0), ("$sc", null), ("$a", 0L), ("$b", 0L));
        using var update = Cmd(c, "UPDATE spans SET end_ms = $b WHERE id = $id", ("$b", 0L), ("$id", 0L));
        insert.Transaction = tx;
        update.Transaction = tx;

        var assigned = new List<(LiveSpan Span, long Id)>();
        foreach (var s in spans)
        {
            if (s.Id == 0)
            {
                insert.Parameters["$p"].Value = s.ProjectId;
                insert.Parameters["$s"].Value = s.SessionId;
                insert.Parameters["$k"].Value = (int)s.Kind;
                insert.Parameters["$sc"].Value = (object?)s.Scene ?? DBNull.Value;
                insert.Parameters["$a"].Value = Ms(s.StartUtc);
                insert.Parameters["$b"].Value = Ms(s.EndUtc);
                assigned.Add((s, (long)insert.ExecuteScalar()!));
            }
            else
            {
                update.Parameters["$b"].Value = Ms(s.EndUtc);
                update.Parameters["$id"].Value = s.Id;
                update.ExecuteNonQuery();
            }
        }
        tx.Commit();
        foreach (var (span, id) in assigned) span.Id = id;
    }

    public List<SpanRec> LoadSpans()
    {
        using var c = Open();
        using var cmd = Cmd(c, "SELECT id, project_id, session_id, kind, scene, start_ms, end_ms, manual FROM spans WHERE end_ms > start_ms ORDER BY start_ms");
        using var r = cmd.ExecuteReader();
        var list = new List<SpanRec>();
        while (r.Read())
        {
            long a = r.GetInt64(5), b = r.GetInt64(6);
            var start = Local(a);
            // Duration comes from UTC so DST switches never produce negative spans.
            list.Add(new SpanRec(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), (SpanKind)r.GetInt32(3), Str(r, 4),
                start, start + TimeSpan.FromMilliseconds(b - a), r.GetInt64(7) != 0));
        }
        return list;
    }

    static long LocalMs(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();

    public void AddManual(long projectId, SpanKind kind, DateTime startLocal, DateTime endLocal)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        var kinds = kind == SpanKind.Editor ? new[] { SpanKind.Open, SpanKind.Editor } : [kind];
        foreach (var k in kinds)
        {
            using var cmd = Cmd(c, "INSERT INTO spans(project_id, session_id, kind, scene, start_ms, end_ms, manual) VALUES($p, 0, $k, NULL, $a, $b, 1)",
                ("$p", projectId), ("$k", (int)k), ("$a", LocalMs(startLocal)), ("$b", LocalMs(endLocal)));
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // Removes the work (and manually added open time) of one project inside a time range.
    public int DeleteWork(long projectId, DateTime fromLocal, DateTime toLocal)
    {
        using var c = Open();
        using var cmd = Cmd(c, """
            DELETE FROM spans WHERE project_id = $p AND start_ms >= $a AND end_ms <= $b AND (kind <> 0 OR manual = 1)
            """, ("$p", projectId), ("$a", LocalMs(fromLocal)), ("$b", LocalMs(toLocal)));
        return cmd.ExecuteNonQuery();
    }

    public void MergeProject(long from, long into)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var sql in new[]
                 {
                     "UPDATE spans SET project_id = $to WHERE project_id = $from",
                     "UPDATE sessions SET project_id = $to WHERE project_id = $from",
                     "DELETE FROM projects WHERE id = $from",
                 })
        {
            using var cmd = Cmd(c, sql, ("$from", from), ("$to", into));
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public Dictionary<DateOnly, string> LoadNotes()
    {
        using var c = Open();
        using var cmd = Cmd(c, "SELECT day, text FROM notes");
        using var r = cmd.ExecuteReader();
        var map = new Dictionary<DateOnly, string>();
        while (r.Read())
            if (DateOnly.TryParseExact(r.GetString(0), "yyyy-MM-dd", out var d)) map[d] = r.GetString(1);
        return map;
    }

    public void SaveNote(DateOnly day, string text)
    {
        using var c = Open();
        var key = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(text)) Exec(c, "DELETE FROM notes WHERE day = $d", ("$d", key));
        else Exec(c, "INSERT INTO notes(day, text) VALUES($d, $t) ON CONFLICT(day) DO UPDATE SET text = excluded.text", ("$d", key), ("$t", text.TrimEnd()));
    }

    public Dictionary<long, TimeSpan> WorkByProject(DateTime fromUtc, DateTime toUtc)
    {
        using var c = Open();
        using var cmd = Cmd(c, """
            SELECT project_id, SUM(MIN(end_ms, $b) - MAX(start_ms, $a)) FROM spans
            WHERE kind <> 0 AND end_ms > $a AND start_ms < $b GROUP BY project_id
            """, ("$a", Ms(fromUtc)), ("$b", Ms(toUtc)));
        using var r = cmd.ExecuteReader();
        var map = new Dictionary<long, TimeSpan>();
        while (r.Read()) map[r.GetInt64(0)] = TimeSpan.FromMilliseconds(r.GetInt64(1));
        return map;
    }

    public void BackupTo(string file)
    {
        using var c = Open();
        Exec(c, "VACUUM INTO $f", ("$f", file));
    }

    public List<SessionRec> LoadSessions()
    {
        using var c = Open();
        using var cmd = Cmd(c, "SELECT id, project_id, pid, unity_version, start_ms, end_ms FROM sessions ORDER BY start_ms");
        using var r = cmd.ExecuteReader();
        var list = new List<SessionRec>();
        while (r.Read())
            list.Add(new SessionRec(r.GetInt64(0), r.GetInt64(1), r.GetInt32(2), Str(r, 3), Local(r.GetInt64(4)), Local(r.GetInt64(5))));
        return list;
    }

    public TimeSpan WorkBetween(DateTime fromUtc, DateTime toUtc)
    {
        using var c = Open();
        using var cmd = Cmd(c, """
            SELECT COALESCE(SUM(MIN(end_ms, $b) - MAX(start_ms, $a)), 0) FROM spans
            WHERE kind <> 0 AND end_ms > $a AND start_ms < $b
            """, ("$a", Ms(fromUtc)), ("$b", Ms(toUtc)));
        return TimeSpan.FromMilliseconds((long)cmd.ExecuteScalar()!);
    }
}
