using System.Net;
using System.Text;

namespace UnityTimeTracker.Core;

// A printable, self-contained HTML report for one period (all projects or one).
public static class Report
{
    const string Style = """
        body { font-family: Arial, sans-serif; font-size: 13px; color: #222; margin: 32px; max-width: 980px; }
        h1 { font-size: 22px; margin: 0 0 4px; }
        h2 { font-size: 15px; margin: 28px 0 8px; border-bottom: 1px solid #ccc; padding-bottom: 4px; }
        .meta { color: #777; margin-bottom: 20px; }
        table { border-collapse: collapse; width: 100%; }
        th { text-align: left; font-weight: bold; border-bottom: 1px solid #bbb; padding: 4px 8px; }
        td { padding: 4px 8px; border-bottom: 1px solid #eee; vertical-align: top; }
        td.num, th.num { text-align: right; white-space: nowrap; }
        .bar { background: #eee; height: 8px; width: 160px; display: inline-block; }
        .bar span { display: block; height: 8px; }
        .dim { color: #888; }
        .kv td:first-child { color: #666; width: 180px; }
        .sw { display: inline-block; width: 9px; height: 9px; margin-right: 6px; }
        """;

    public static string Html(DataSnapshot d, Period period, long? projectId)
    {
        var (from, to) = period.Days(d.FirstDay);
        var range = Clock.Range(from, to);
        var all = d.Of(projectId).ToList();
        var work = all.Where(s => s.Kind != SpanKind.Open).ToList();
        var totals = Stats.Sum(all, range);
        var perDay = Stats.PerDay(work, from, to);
        var sessions = d.WorkSessions(projectId).Where(s => range.Contains(s.Start)).ToList();
        var subject = projectId is long id ? d.Get(id)?.Title ?? "Unknown" : "All projects";
        int worked = perDay.Count(kv => Stats.Total(kv.Value) >= TimeSpan.FromMinutes(1));
        var longest = sessions.MaxBy(s => s.Work);

        var sb = new StringBuilder();
        sb.Append($"<!doctype html><html><head><meta charset=\"utf-8\"><title>{E(subject)}, {E(period.Label)}</title><style>{Style}</style></head><body>");
        sb.Append($"<h1>Unity time report</h1><div class=\"meta\">{E(subject)}, {E(period.Label)}. Generated {DateTime.Now:yyyy-MM-dd HH:mm}.</div>");

        sb.Append("<table class=\"kv\">");
        Row("Total", Fmt.Dur(totals.Work));
        Row("In Unity", Fmt.Dur(totals.Editor));
        Row("In IDE", Fmt.Dur(totals.Ide));
        Row("Unity open", Fmt.Dur(totals.Open));
        Row("Days worked", worked.ToString());
        Row("Daily average", worked > 0 ? Fmt.Dur(totals.Work / worked) : "-");
        Row("Sessions", sessions.Count.ToString());
        Row("Longest session", longest != null ? $"{Fmt.Dur(longest.Work)} on {Fmt.Day(Clock.DayOf(longest.Start))}" : "-");
        sb.Append("</table>");

        if (projectId == null)
        {
            var per = Stats.PerProject(all, range).Where(kv => kv.Value.Work > TimeSpan.Zero).OrderByDescending(kv => kv.Value.Work).ToList();
            if (per.Count > 0)
            {
                var max = per[0].Value.Work;
                sb.Append("<h2>Projects</h2><table><tr><th>Project</th><th></th><th class=\"num\">Total</th><th class=\"num\">In Unity</th><th class=\"num\">In IDE</th><th class=\"num\">Share</th></tr>");
                foreach (var (pid, t) in per)
                {
                    var p = d.Get(pid);
                    sb.Append($"<tr><td><span class=\"sw\" style=\"background:{p?.Color}\"></span>{E(p?.Title ?? "Unknown")}</td><td>{Bar(t.Work / max, p?.Color)}</td>")
                      .Append($"<td class=\"num\">{Fmt.Dur(t.Work)}</td><td class=\"num\">{Fmt.Dur(t.Editor)}</td><td class=\"num\">{Fmt.Dur(t.Ide)}</td><td class=\"num\">{Fmt.Pct(t.Work / totals.Work)}</td></tr>");
                }
                sb.Append("</table>");
            }
        }

        var days = perDay.Keys.Union(d.Notes.Keys.Where(k => k >= from && k < to)).Order().ToList();
        if (days.Count > 0)
        {
            var maxDay = perDay.Count > 0 ? perDay.Values.Max(Stats.Total) : TimeSpan.Zero;
            sb.Append("<h2>Days</h2><table><tr><th>Date</th><th></th><th class=\"num\">Total</th><th>Projects</th><th>Note</th></tr>");
            foreach (var day in days)
            {
                var map = perDay.GetValueOrDefault(day);
                var total = Stats.Total(map);
                var projects = map == null ? "" : string.Join(", ", map.OrderByDescending(kv => kv.Value).Select(kv => $"{d.Get(kv.Key)?.Title} {Fmt.Dur(kv.Value)}"));
                sb.Append($"<tr><td>{E(Fmt.Day(day))}</td><td>{Bar(maxDay > TimeSpan.Zero ? total / maxDay : 0, "#3A79BB")}</td><td class=\"num\">{Fmt.Dur(total)}</td>")
                  .Append($"<td class=\"dim\">{E(projects)}</td><td>{E(d.Notes.GetValueOrDefault(day) ?? "").Replace("\n", "<br>")}</td></tr>");
            }
            sb.Append("</table>");
        }

        var scenes = Stats.Scenes(all, range).Take(15).ToList();
        if (scenes.Count > 0)
        {
            sb.Append("<h2>Top scenes</h2><table><tr><th>Scene</th><th>Project</th><th class=\"num\">In Unity</th></tr>");
            foreach (var (pid, scene, time) in scenes)
                sb.Append($"<tr><td>{E(scene)}</td><td class=\"dim\">{E(d.Get(pid)?.Title ?? "")}</td><td class=\"num\">{Fmt.Dur(time)}</td></tr>");
            sb.Append("</table>");
        }

        if (sessions.Count > 0)
        {
            sb.Append("<h2>Sessions</h2><table><tr><th>Date</th><th>Project</th><th>Time</th><th class=\"num\">Total</th><th>Scenes</th></tr>");
            foreach (var s in sessions)
            {
                var scenesText = string.Join(", ", s.Scenes.OrderByDescending(kv => kv.Value).Select(kv => kv.Key));
                if (s.Manual) scenesText = scenesText.Length > 0 ? scenesText + ", added manually" : "added manually";
                sb.Append($"<tr><td>{E(Fmt.Day(Clock.DayOf(s.Start)))}</td><td>{E(d.Get(s.ProjectId)?.Title ?? "")}</td>")
                  .Append($"<td>{Fmt.Time(s.Start)} - {Fmt.Time(s.End)}</td><td class=\"num\">{Fmt.Dur(s.Work)}</td><td class=\"dim\">{E(scenesText)}</td></tr>");
            }
            sb.Append("</table>");
        }

        sb.Append("</body></html>");
        return sb.ToString();

        void Row(string k, string v) => sb.Append($"<tr><td>{E(k)}</td><td>{E(v)}</td></tr>");
    }

    static string Bar(double fraction, string? color) =>
        $"<span class=\"bar\"><span style=\"width:{Math.Clamp(fraction, 0, 1) * 100:0.#}%;background:{color ?? "#3A79BB"}\"></span></span>";

    static string E(string s) => WebUtility.HtmlEncode(s);
}
