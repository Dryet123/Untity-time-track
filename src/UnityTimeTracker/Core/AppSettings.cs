using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnityTimeTracker.Core;

public sealed class AppSettings
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public int IdleMinutes { get; set; } = 5;
    public bool TrackIde { get; set; } = true;
    public int DayStartHour { get; set; }
    public bool Paused { get; set; }
    public bool AutostartRegistered { get; set; }
    public bool TrayHintShown { get; set; }
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 840;
    public bool WindowMaximized { get; set; }
    public int DailyGoalMinutes { get; set; }
    public int BreakReminderMinutes { get; set; }
    public string LastTab { get; set; } = "Overview";
    public PeriodKind OverviewPeriod { get; set; } = PeriodKind.Week;
    public PeriodKind ProjectPeriod { get; set; } = PeriodKind.All;
    public PeriodKind LogPeriod { get; set; } = PeriodKind.Month;
    public DateTime LastBackup { get; set; }

    [JsonIgnore]
    public TimeSpan DailyGoal => TimeSpan.FromMinutes(DailyGoalMinutes);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.Settings))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.Settings)) ?? new();
        }
        catch (Exception ex) { Log.Error(ex); }
        return new();
    }

    public void Save()
    {
        try
        {
            var tmp = AppPaths.Settings + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, AppPaths.Settings, true);
        }
        catch (Exception ex) { Log.Error(ex); }
    }
}
