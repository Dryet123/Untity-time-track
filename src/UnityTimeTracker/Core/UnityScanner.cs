using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;

namespace UnityTimeTracker.Core;

public sealed record EditorInfo(int Pid, string? ProjectPath, string? TitleProject, string? Scene, string? Version);

public sealed partial class UnityScanner
{
    sealed record ProcInfo(bool IsEditor, string? ProjectPath, string? Version);

    static readonly string[] Platforms =
    [
        "Windows, Mac, Linux", "Android", "iOS", "WebGL", "Web", "Universal Windows Platform", "tvOS", "visionOS",
        "PS4", "PS5", "Xbox One", "Xbox Series", "Nintendo Switch", "Linux", "macOS", "Dedicated Server", "Embedded Linux", "QNX",
    ];

    readonly Dictionary<int, ProcInfo> known = [];

    public List<EditorInfo> Scan()
    {
        var alive = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName("Unity"))
        {
            using (p)
            {
                alive.Add(p.Id);
                if (!known.ContainsKey(p.Id)) known[p.Id] = Inspect(p.Id);
            }
        }
        foreach (var pid in known.Keys.Where(k => !alive.Contains(k)).ToList()) known.Remove(pid);

        var editors = known.Where(k => k.Value.IsEditor).Select(k => k.Key).ToHashSet();
        if (editors.Count == 0) return [];

        var titles = MainTitles(editors);
        var result = new List<EditorInfo>();
        foreach (var pid in editors)
        {
            var info = known[pid];
            var folderName = info.ProjectPath != null ? Path.GetFileName(info.ProjectPath) : null;
            var (project, scene) = titles.TryGetValue(pid, out var title) ? ParseTitle(title, folderName) : (null, null);
            result.Add(new EditorInfo(pid, info.ProjectPath, project, scene, info.Version));
        }
        return result;
    }

    static ProcInfo Inspect(int pid)
    {
        string? cmd = null, exe = null;
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT CommandLine, ExecutablePath FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (var o in searcher.Get())
            {
                using (o)
                {
                    cmd = o["CommandLine"] as string;
                    exe = o["ExecutablePath"] as string;
                }
            }
        }
        catch (Exception ex) { Log.Error(ex); }

        // Asset import workers and command-line builds are Unity.exe too.
        if (cmd != null && (cmd.Contains("-batchmode", StringComparison.OrdinalIgnoreCase)
                            || cmd.Contains("AssetImportWorker", StringComparison.OrdinalIgnoreCase)))
            return new ProcInfo(false, null, null);

        string? path = null;
        if (cmd != null && ProjectArg().Match(cmd) is { Success: true } m)
            path = ProjectPaths.Normalize(m.Groups["p"].Value);
        return new ProcInfo(true, path, VersionOf(exe));
    }

    static string? VersionOf(string? exe)
    {
        if (exe == null) return null;
        var dir = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(exe)));
        if (dir != null && VersionDir().IsMatch(dir)) return dir;
        try { return FileVersionInfo.GetVersionInfo(exe).ProductVersion?.Split('_')[0]; }
        catch { return null; }
    }

    static Dictionary<int, string> MainTitles(HashSet<int> pids)
    {
        var map = new Dictionary<int, string>();
        Native.EnumWindows((hWnd, _) =>
        {
            Native.GetWindowThreadProcessId(hWnd, out var pid);
            if (pids.Contains((int)pid) && !map.ContainsKey((int)pid) && Native.IsWindowVisible(hWnd))
            {
                var title = Native.WindowText(hWnd);
                if (title.Contains(" - Unity ", StringComparison.Ordinal)) map[(int)pid] = title;
            }
            return true;
        }, IntPtr.Zero);
        return map;
    }

    // Unity 6: "Project - Scene - Platform - Unity 6000.x <DX12>"; 2022 and older: "Scene - Project - Platform - Unity ...".
    public static (string? Project, string? Scene) ParseTitle(string title, string? knownProject)
    {
        int cut = title.LastIndexOf(" - Unity ", StringComparison.Ordinal);
        if (cut < 0) return (null, null);
        var parts = title[..cut].Split(" - ").Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        string? project = null;
        if (knownProject != null)
        {
            int i = parts.FindIndex(x => x.Equals(knownProject, StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
            {
                project = parts[i];
                parts.RemoveAt(i);
            }
        }
        if (project == null && parts.Count > 0)
        {
            project = parts[0];
            parts.RemoveAt(0);
        }

        if (parts.Count > 0 && (parts.Count > 1 || Platforms.Contains(parts[^1], StringComparer.OrdinalIgnoreCase)))
            parts.RemoveAt(parts.Count - 1);

        var scene = string.Join(" - ", parts).TrimEnd('*').Trim();
        return (project, scene.Length > 0 ? scene : null);
    }

    [GeneratedRegex(@"-(?:projectpath|createproject)\s+(?:""(?<p>[^""]+)""|(?<p>\S+))", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectArg();

    [GeneratedRegex(@"^\d{4}\.\d+\.\d+[abfpx]\d+")]
    private static partial Regex VersionDir();
}
