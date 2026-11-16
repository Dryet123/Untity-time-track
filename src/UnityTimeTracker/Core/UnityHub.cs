using System.Text.Json;

namespace UnityTimeTracker.Core;

public static class UnityHub
{
    static string HubDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnityHub");

    public static int Import(Database db)
    {
        var file = Path.Combine(HubDir, "projects-v1.json");
        if (!File.Exists(file)) return 0;

        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return 0;

        int count = 0;
        foreach (var entry in data.EnumerateObject())
        {
            var e = entry.Value;
            if (e.ValueKind != JsonValueKind.Object) continue;
            var folder = ProjectPaths.Normalize(Str(e, "path") ?? entry.Name);
            var name = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(name)) continue;
            db.UpsertHubProject(new HubProject(folder, name, Str(e, "version"), Str(e, "renderPipeline"), Str(e, "buildTarget"), Str(e, "repositoryName")));
            count++;
        }
        return count;
    }

    public static string? FindEditor(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var roots = new List<string>();
        try
        {
            var custom = Path.Combine(HubDir, "secondaryInstallPath.json");
            if (File.Exists(custom) && JsonSerializer.Deserialize<string>(File.ReadAllText(custom)) is { Length: > 0 } dir)
                roots.Add(dir);
        }
        catch (JsonException) { }
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor"));

        return roots.Select(r => Path.Combine(r, version, "Editor", "Unity.exe")).FirstOrDefault(File.Exists);
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}
