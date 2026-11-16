using System.Text.Json;

namespace UnityTimeTracker.Core;

public sealed class ProjectDetails
{
    public bool Exists { get; init; }
    public string? UnityVersion { get; set; }
    public string? ProductName { get; set; }
    public string? Company { get; set; }
    public string? AppVersion { get; set; }
    public string? Pipeline { get; set; }
    public int Packages { get; set; }
    public string? GitBranch { get; set; }
    public string? GitRemote { get; set; }
    public int Scripts { get; set; }
    public long ScriptLines { get; set; }
    public int Scenes { get; set; }
    public int Prefabs { get; set; }
    public long AssetsBytes { get; set; }
}

// Reads what can be known about a Unity project straight from its folder.
public static class ProjectInspector
{
    public static ProjectDetails Inspect(string folder, CancellationToken ct)
    {
        var d = new ProjectDetails { Exists = Directory.Exists(folder) };
        if (!d.Exists) return d;

        d.UnityVersion = YamlValue(Path.Combine(folder, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion");
        var settings = Path.Combine(folder, "ProjectSettings", "ProjectSettings.asset");
        d.ProductName = YamlValue(settings, "productName");
        d.Company = YamlValue(settings, "companyName");
        d.AppVersion = YamlValue(settings, "bundleVersion");
        ReadManifest(Path.Combine(folder, "Packages", "manifest.json"), d);
        ReadGit(Path.Combine(folder, ".git"), d);

        var assets = Path.Combine(folder, "Assets");
        if (!Directory.Exists(assets)) return d;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in new DirectoryInfo(assets).EnumerateFiles("*", options))
        {
            ct.ThrowIfCancellationRequested();
            d.AssetsBytes += file.Length;
            switch (file.Extension.ToLowerInvariant())
            {
                case ".cs":
                    d.Scripts++;
                    try { d.ScriptLines += File.ReadLines(file.FullName).LongCount(); }
                    catch (IOException) { }
                    break;
                case ".unity": d.Scenes++; break;
                case ".prefab": d.Prefabs++; break;
            }
        }
        return d;
    }

    static string? YamlValue(string file, string key)
    {
        if (!File.Exists(file)) return null;
        try
        {
            foreach (var line in File.ReadLines(file))
            {
                var t = line.Trim();
                if (t.StartsWith(key + ":", StringComparison.Ordinal))
                {
                    var v = t[(key.Length + 1)..].Trim();
                    return v.Length > 0 ? v : null;
                }
            }
        }
        catch (IOException) { }
        return null;
    }

    static void ReadManifest(string file, ProjectDetails d)
    {
        d.Pipeline = "Built-in";
        if (!File.Exists(file)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("dependencies", out var deps) || deps.ValueKind != JsonValueKind.Object) return;
            foreach (var dep in deps.EnumerateObject())
            {
                d.Packages++;
                if (dep.Name == "com.unity.render-pipelines.universal") d.Pipeline = "URP";
                else if (dep.Name == "com.unity.render-pipelines.high-definition") d.Pipeline = "HDRP";
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
    }

    static void ReadGit(string gitDir, ProjectDetails d)
    {
        try
        {
            var head = Path.Combine(gitDir, "HEAD");
            if (File.Exists(head))
            {
                var text = File.ReadAllText(head).Trim();
                d.GitBranch = text.StartsWith("ref: refs/heads/", StringComparison.Ordinal) ? text[16..] : text[..Math.Min(7, text.Length)];
            }

            var config = Path.Combine(gitDir, "config");
            if (!File.Exists(config)) return;
            bool inOrigin = false;
            foreach (var raw in File.ReadLines(config))
            {
                var line = raw.Trim();
                if (line.StartsWith('[')) inOrigin = line == "[remote \"origin\"]";
                else if (inOrigin && line.StartsWith("url", StringComparison.Ordinal) && line.IndexOf('=') is var eq and > 0)
                {
                    d.GitRemote = line[(eq + 1)..].Trim();
                    return;
                }
            }
        }
        catch (IOException) { }
    }
}
