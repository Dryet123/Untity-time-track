using System.Diagnostics;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using UnityTimeTracker.Core;
using UnityTimeTracker.UI.Controls;
using UnityTimeTracker.UI.Dialogs;

namespace UnityTimeTracker.UI;

// Data-changing commands shared by several tabs. Each returns true when something changed.
public static class Actions
{
    public static bool AddTime(DependencyObject from, DataSnapshot snap, DateOnly day, long? projectId)
    {
        var dialog = new AddTimeDialog(snap, day, projectId) { Owner = Window.GetWindow(from) };
        if (dialog.ShowDialog() != true) return false;
        App.Db.AddManual(dialog.ProjectId, dialog.Kind, dialog.Start, dialog.End);
        return true;
    }

    public static bool DeleteSession(DependencyObject from, SessionRow row)
    {
        var answer = MessageBox.Show(Window.GetWindow(from),
            $"Delete this session?\n\n{row.Name}, {row.Date}, {row.Range}, {row.WorkText}\n\n" +
            "Its Unity and IDE time is removed. How long Unity was open stays as it was. This can't be undone.",
            "Delete session", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return false;

        App.Tracker.Flush();
        App.Tracker.RestartWork(row.Session.ProjectId);
        App.Db.DeleteWork(row.Session.ProjectId, row.Start, row.End);
        return true;
    }

    public static bool Merge(DependencyObject from, DataSnapshot snap, Project source)
    {
        var targets = snap.Projects.Where(p => p.Id != source.Id).ToList();
        if (targets.Count == 0) return false;
        var dialog = new PickProjectDialog("Merge project",
            $"Move all time of \"{source.Title}\" into another project, then remove \"{source.Title}\". " +
            "Handy when a project folder was moved or renamed and shows up twice.",
            "Merge", targets) { Owner = Window.GetWindow(from) };
        if (dialog.ShowDialog() != true || dialog.Picked is not Project target) return false;

        App.Tracker.Flush();
        App.Tracker.Forget(source.Id);
        App.Db.MergeProject(source.Id, target.Id);
        IconStore.Delete(source.Icon);
        App.Tracker.ReloadProjects();
        return true;
    }

    public static void SaveReport(DependencyObject from, DataSnapshot snap, Period period, long? projectId)
    {
        var subject = projectId is long id ? snap.Get(id)?.Title ?? "project" : "all-projects";
        var name = string.Concat($"unity-time-{subject}-{period.Label}".Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-'));
        while (name.Contains("--")) name = name.Replace("--", "-");
        var dialog = new SaveFileDialog { Title = "Save report", Filter = "Web page|*.html", FileName = name.Trim('-') + ".html" };
        if (dialog.ShowDialog(Window.GetWindow(from)) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, Report.Html(snap, period, projectId), new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            MessageBox.Show(Window.GetWindow(from), ex.Message, "Report", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
