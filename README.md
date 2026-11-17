# Unity Time Tracker

A Windows time tracker for the Unity editor, built with WPF, .NET 10 and SQLite. It runs in the tray, finds running Unity editors on its own and keeps a log of where your time goes.

## How it tracks

Every second the tracker looks for running Unity editors. The project comes from the editor's `-projectPath` argument and the scene from its window title. Three kinds of time are kept apart: how long Unity was open, how long a Unity window was in front while you were actually using the keyboard or mouse, and time spent in Rider, Visual Studio, VS Code or Cursor when the window title mentions the project. Idle stretches and sleep are not counted.

## Overview

Totals for a day, week, month, year or all time, compared with the previous period. Daily goal progress, focus stats (uninterrupted stretches in Unity and how often you switched away), time per day, hour of day and weekday, a weekday by hour rhythm grid, session lengths, and the top projects and scenes.

## Calendar

A year heatmap, a month view that marks goal days and days with notes, a timeline for the selected day, a note for each day and a way to add time the tracker missed.

## Projects

Details read straight from the project folder: Unity version and which editor versions were used, render pipeline, git branch and remote, scripts and lines of code, scenes, prefabs and asset size. Each project can get its own icon, color and display name, and two projects can be merged when a folder was moved or renamed. The same charts as the overview, plus a report, for one project.

## Log

Every work session with its scenes, filterable by project or scene. Sessions can be added by hand or deleted, exported to CSV, or turned into a printable HTML report for the period.

## Tray and background

The tray menu shows today's time per project, the icon shows whether you are working right now, and an optional reminder suggests a break after a long stretch. The database is copied once a day and the last 14 copies are kept.

## Autostart

The app registers itself in the registry `Run` key, so it appears in Task Manager under Startup apps as Unity Time Tracker and can be turned on or off there or in Settings. It starts minimized to the tray.

## Keyboard

| Keys | Action |
| --- | --- |
| Ctrl+1 to Ctrl+5 | Switch tabs |
| Ctrl+Left, Ctrl+Right | Previous or next period |
| Ctrl+Home | Back to the current period |
| Delete | Delete the selected session in Log |
| F5 | Reload |

## Install

```powershell
.\publish.ps1
```

This builds a Release copy into `%LOCALAPPDATA%\Programs\UnityTimeTracker`, closes the running copy and starts the new one. The Release build registers autostart on its first run.

Data lives in `%LOCALAPPDATA%\UnityTimeTracker` (`tracker.db`, `settings.json`, `icons\`, `backups\`). Set the `UTT_DATA_DIR` environment variable to point a test copy at another folder; a copy with its own data folder runs as a separate instance.
