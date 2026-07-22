# M2_APEX

**English** · [繁體中文](README_TW.md)

> A blazing-fast file search and app launcher for Windows, inspired by [Listary](https://www.listary.com/).

M2_APEX lives in the system tray and summons a search bar anywhere via a global hotkey, fuzzy-searching your
whole PC — files, apps, system commands and the web — in milliseconds. It also offers Listary's signature
**Quick Switch**: just start typing in File Explorer to instantly filter, highlight and jump to the matching file.

Tech: **.NET 9 · WPF · WinForms (tray)** — pure managed code with Win32/COM/UI Automation interop, no third-party dependencies.
Branding: the tray icon, window icon and search bar all use the **M2** logo (drawn as vector art; see `Assets/M2Logo.cs`).

---

## Features

| Category | Description |
| --- | --- |
| **Global activation** | Double-tap `Ctrl`, or press `Alt+Space`, to summon the search bar anywhere |
| **File / folder search** | Background-indexes all fixed drives; millisecond fuzzy search; results cached to disk for instant use on startup |
| **App launching** | Scans Start Menu shortcuts (`.lnk` / `.url` / `.appref-ms`) |
| **Web search** | One key to search with your default engine when nothing matches or on demand; customizable URL |
| **System commands** | Lock, sleep, shut down, restart, sign out, Recycle Bin, empty Recycle Bin, Settings, Control Panel, Task Manager |
| **Fuzzy matching** | Prefix, word-boundary, camelCase and acronym matches (e.g. `vsc` → Visual Studio Code), with matched characters **highlighted** |
| **Habit ranking** | Frequently and recently used results float to the top |
| **Result actions** | Open, open containing folder, run as administrator, copy path |
| **Quick Switch** | Type in the File Explorer file list → a highlighted list pops up → jump to / select the matching file |
| **Tray resident** | Runs in the background; right-click menu to open search, rebuild index, open settings, exit |
| **Settings window** | Hotkeys, Quick Switch, result count, search engine, indexed drives, excluded folders, bar positions, launch at startup, and more |

---

## Shortcuts & usage

### Search bar (global)

| Key | Action |
| --- | --- |
| `Ctrl` `Ctrl` (double-tap) / `Alt+Space` | Open the search bar |
| Type | Search live |
| `↑` / `↓`, `PageUp` / `PageDown` | Move the selection |
| `Enter` | Open the selected item |
| `Ctrl+Enter` | Open containing folder |
| `Shift+Enter` | Run as administrator |
| `Ctrl+C` | Copy path |
| `Esc` | Close |

### Quick Switch (inside File Explorer)

| Key | Action |
| --- | --- |
| Type in the file list | Pop up a highlighted, filtered list |
| `↑` / `↓` | Move between matches (Explorer's selection follows) |
| `Enter` | Open / enter in place |
| `Backspace` | Edit the keyword |
| Click an item | Open that item |
| `Esc` / click another window | Close |

> Quick Switch only acts while the file list has focus; typing in the address bar, search box, or a rename (F2)
> text field is completely unaffected.

### M2_Commander (multi-pane file manager)

Open with `Ctrl` + `` ` `` (backtick) from any search surface. Fully keyboard-driven:

| Key | Action |
| --- | --- |
| `Tab` | Switch the active pane |
| `Alt+←` / `Alt+→` | Focus the pane on the left / right |
| `Enter` | Open file / enter folder |
| `Backspace` / `Alt+↑` | Go to the parent folder |
| `Alt+[` | Back (history) |
| `Alt+]` | Forward (history) |
| Type `A`–`Z` | Filter the list |
| `Esc` | Clear the filter |
| `Ctrl+C` / `Ctrl+V` | Mark for copy / paste into the active pane |
| `F2` | Rename |
| `Del` | Delete permanently (bypasses Recycle Bin) |
| `Shift+Del` | Fast-delete a folder |
| `Ctrl+U` | Swap the two panes |
| `Ctrl+R` | Refresh both panes |
| `F1` | Actions menu (copy / move / delete / new…) |
| `F11` | Custom command settings |
| `F12` | All keyboard shortcuts |
| `F10` | Quit |

> Click **+** at the right to open up to **4 panes**; **×** on a pane header closes it (minimum 2). The window
> remembers its size, position, pane count and each pane's folder, and scales to the screen. Move and swap apply
> to a two-pane layout.

---

## Requirements

- Windows 10 / 11 (x64; for ARM64 see the "Build & run" section)
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) (not needed for a self-contained release)

---

## Build & run

```powershell
# Build
dotnet build -c Release

# Run directly
dotnet run -c Release
# Or run the built executable
.\bin\Release\net9.0-windows\M2_APEX.exe
```

### Publish as a single self-contained file (no .NET install required)

```powershell
# x64
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# ARM64 (Windows on ARM)
dotnet publish -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true
```

> The project is pure managed code; every native call targets Windows system DLLs (`user32` / `shell32` / `kernel32`)
> and COM / UI Automation, so it is **architecture-neutral** and cross-compiles to ARM64 directly.

### Releases & updates

Pushing a `v*.*.*` tag runs the GitHub Actions **Release** workflow, which publishes self-contained single-file
EXEs for **x64** and **ARM64** to the GitHub Releases page. The Settings window shows the current version and a
**Check for updates** button (built on the GitHub Releases API; see `Services/UpdateService.cs`). Every push and
pull request is also compiled by the **Build** CI workflow.

---

## Project structure

```
M2_APEX/
├─ App.xaml(.cs)              # Entry point, tray, single instance, crash logging, service wiring
├─ app.manifest               # DPI awareness / asInvoker / Win10-11 compatibility
├─ Assets/
│  ├─ M2Logo.cs               # M2 logo (WPF geometry / image / bitmap source)
│  └─ m2-logo.svg             # Original SVG logo
├─ Models/
│  ├─ AppSettings.cs          # User settings (JSON persistence)
│  └─ SearchResult.cs         # Search result model
├─ Services/
│  ├─ FuzzyMatcher.cs         # Fuzzy match scoring + matched indices (for highlighting)
│  ├─ FileIndexService.cs     # Whole-drive BFS index + disk cache
│  ├─ AppIndexService.cs      # Start Menu app scan
│  ├─ SearchEngine.cs         # Merges apps / files / commands / web and ranks
│  ├─ CommandProvider.cs      # Built-in system commands
│  ├─ UsageTracker.cs         # Habit (frequency + recency) ranking
│  ├─ LaunchService.cs        # Open / open folder / admin / copy path
│  ├─ HotkeyService.cs        # Low-level keyboard hook (double-Ctrl / Alt+Space / key interception)
│  ├─ QuickSwitchService.cs   # File-Explorer type-to-jump logic
│  ├─ ExplorerAccess.cs       # Shell.Application COM + UI Automation
│  ├─ IconGlyph.cs            # Maps file extensions to Segoe MDL2 glyphs
│  ├─ AppIcon.cs              # Renders the tray icon from the M2 logo
│  ├─ UpdateService.cs        # Checks GitHub Releases for a newer version
│  └─ StartupService.cs       # Launch at startup (registry key)
├─ ViewModels/
│  └─ SearchViewModel.cs      # Search-bar MVVM (debounce, selection)
├─ Views/
│  ├─ SearchWindow.xaml(.cs)      # Main search bar
│  ├─ QuickSwitchBar.xaml(.cs)    # Quick Switch highlighted-list popup
│  └─ SettingsWindow.xaml(.cs)    # Settings window
├─ Behaviors/
│  ├─ Highlight.cs                # Attached property that highlights matched characters in a TextBlock
│  └─ KindToLabelConverter.cs
└─ Native/
   └─ NativeMethods.cs        # Win32 P/Invoke declarations
```

---

## Settings & data location

All data lives in `%APPDATA%\M2_APEX`:

| File | Contents |
| --- | --- |
| `settings.json` | User settings |
| `usage.json` | Usage habits (frequency / recency) |
| `index.cache` | File index cache |
| `crash.log` | Unhandled-exception log (if any) |

The Settings window lets you adjust: double-Ctrl / Alt+Space, Quick Switch, max result count, web search URL,
indexed drives (empty = all fixed drives), excluded folders, whether to index hidden files, search-bar and
Quick Switch positions, and launch at startup — plus a manual "Rebuild index".

---

## How it works (overview)

- **Global hotkeys / key interception**: a `WH_KEYBOARD_LL` low-level keyboard hook detects double-Ctrl and
  Alt+Space, and intercepts input while the Explorer file list has focus to drive Quick Switch (everything else
  passes through, so normal typing is never affected).
- **File indexing**: a queued BFS enumerates fixed drives, applying the exclude list and hidden-file attribute;
  results are saved as `index.cache` and loaded on next startup, with manual rebuild available.
- **Fuzzy matching**: greedy subsequence + word-boundary / camelCase / acronym weighted scoring; matched indices
  are computed only at display time for highlighting; search picks top-K in parallel.
- **Quick Switch**: reads the current folder via `Shell.Application` COM (matching / open in place) and uses UI
  Automation to select and scroll to the matching item (gracefully degrading to Enter-to-open on failure).

---

## Known limitations

- Quick Switch currently matches the "current folder" contents (not a global cross-folder search).
- Quick Switch key-character mapping currently covers A–Z, 0–9, space, `.` `-` `_` (full symbols / international
  keyboard layouts are not yet supported).
- File indexing is load-on-startup + manual rebuild (no live file-system watching).
- The first full-PC index takes a few seconds (depending on file count).

---

## License

A sample project for learning and personal use only; "Listary" is a trademark of its original author and this
project is not affiliated with it. The M2 logo is taken from the same author's M2Station/M2_GIT_DIFF project.
