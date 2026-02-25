# Watchdog Mode - Process Tracking Guide

## Overview
In Watchdog Mode, TaskCore runs a minimal memory footprint (~100MB) with only **Rules** and **Settings** tabs. The process tracking works **exactly the same** as normal mode - all processes are tracked and stored.

## ⚠️ IMPORTANT: UsageTrackingService Tracks EVERYTHING

**Same in both modes:**
- ✅ All user apps (Chrome, Office, Games, etc.)
- ✅ All system processes (svchost, csrss, etc.)
- ✅ All background services (OneDrive, Antivirus, etc.)
- ✅ All sessions saved with idle/active markers
- ✅ Full session history available when you switch back to normal mode

**The only difference is UI - not data collection:**
- ❌ Dashboard view not loaded (but data still there)
- ❌ Process list UI not loaded (but tracking continues)
- ❌ Graphs not loaded (but data still there)

## What's Removed in Watchdog Mode (UI Only)

### ❌ Views Not Loaded
```
DashboardView      - Not shown, but data tracked
ProcessesView      - Not shown, but data tracked  
GraphsView         - Not shown, but data tracked
DetailsView        - Not shown
```

### ❌ Services Not Initialized
```
LiveCharts/SkiaSharp    - ~35MB saved (graphs only)
HardwareMonitorService  - ~20MB saved (temps only)
Graph ViewModels        - ~30MB saved
ProcessService Timer    - Slowed to 5s (vs 1-2s)
```

### ✅ Services Still Running
```
UsageTrackingService   - FULL tracking (all processes)
RulesService           - FULL rules engine
SessionTracker         - FULL session recording
Storage                - FULL JSONL saving
```

## Memory Savings Breakdown

| Component | Normal Mode | Watchdog Mode | Impact on Data |
|-----------|-------------|---------------|----------------|
| LiveCharts/SkiaSharp | ~35MB | ❌ Not loaded | None - graphs not shown |
| Dashboard Views | ~25MB | ❌ Not loaded | None - data still tracked |
| Graph ViewModels | ~30MB | ❌ Not loaded | None - data still tracked |
| HardwareMonitorService | ~20MB | ⚠️ Paused | None - temps not needed |
| UsageTrackingService | ~15MB | ✅ Running | **Full tracking** |
| RulesService | ~10MB | ✅ Running | **Full rules engine** |
| **Total Baseline** | **~350MB** | **~100MB** | **Same data quality** |

## Rules That Work in Watchdog Mode

### ✅ Fully Functional (All Rules Work)
1. **Process Running** - Detect when any app starts
2. **Process Not Running** - Detect when any app closes
3. **Kill Process** - Terminate any process (including system!)
4. **Set Priority** - Change any process priority
5. **Log Only** - Record all events
6. **Notify** - Show toast notifications
7. **Memory Above** - Track memory usage
8. **CPU Above** - Track CPU usage

### Process Filtering
- **Filter Mode**: `ExcludeWindows` (default)
- **What it does**: Only affects what's shown in Rules UI dropdown
- **What it doesn't do**: Doesn't affect what's tracked or what rules can act on
- **Rules can target ANY process**, including system processes

## Example Rules for Watchdog Mode

### Block Time-Wasting Sites
```
Rule: "Limit Social Media"
- Target: chrome.exe, firefox.exe
- Condition: Process Running + Window Title contains "facebook"
- Action: Kill Process after 30 minutes
- Works in watchdog: YES (full tracking)
```

### Prevent Background Apps
```
Rule: "No Games During Work"
- Target: steam.exe, epicgameslauncher.exe
- Condition: Process Running
- Action: Kill Process Immediately
- Works in watchdog: YES (full tracking)
```

### System Process Alert
```
Rule: "CPU Hog Alert"
- Target: (blank = all processes including system)
- Condition: CPU Above 50%
- Action: Notify + Log
- Works in watchdog: YES (tracks everything)
```

## Data Persistence

### When You Switch Back to Normal Mode
```
✅ All session history visible
✅ All usage data intact
✅ Idle/active markers show correctly
✅ Dashboard shows complete data
✅ Graphs show complete data
```

### Session Files Location
```
%LOCALAPPDATA%\TaskCore\SessionLogs\sessions-YYYY-MM-DD.jsonl
```

**Same format, same data, same quality in both modes.**

## Technical Details

### UsageTrackingService (Always Full Power)
```csharp
// Runs IDENTICALLY in both modes
_timer.Interval = TimeSpan.FromSeconds(1);
TickCore() // Tracks ALL processes
_sessionTracker.ProcessTick() // Records ALL sessions
UsageTrackingStorage.AppendSession() // Saves ALL data
```

### What Changes
```csharp
// Normal Mode
LiveCharts.Configure(config => config.AddSkiaSharp()); // ~35MB
var dashboard = new DashboardView(); // ~25MB
var graphs = new GraphsView(); // ~30MB
HardwareMonitorService.Instance; // ~20MB

// Watchdog Mode
// Skip all of the above - saves ~110MB
// UsageTrackingService still runs at full power
```

## When to Use Watchdog Mode

### ✅ Recommended
- Only need Rules Engine (killing, alerting, logging)
- Running on low-RAM system
- Background monitoring 24/7
- Server/headless environment
- Don't need to see graphs/dashboard

### ❌ Not Recommended
- Need to view real-time graphs
- Want to browse session history NOW
- Need detailed process metrics NOW
- Want to see dashboard cards

**Note: You can switch back anytime and see all data!**

## Switching Back to Normal Mode

1. Go to **Settings** tab
2. Toggle OFF "Minimal Watchdog Mode"
3. Click **Yes** to restart
4. All data preserved, full UI restored
5. Dashboard shows complete history
