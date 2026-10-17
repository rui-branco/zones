<p align="center">
  <img src="docs/logo.png" alt="" width="104" height="104">
</p>

<h1 align="center">Zones</h1>

<p align="center">
  <strong>Snap windows into zones you draw yourself, on Windows and macOS.</strong><br>
  Hold Shift, drag a window, drop it in a zone.
</p>

<p align="center">
  <img alt="Windows" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4">
  <img alt="macOS" src="https://img.shields.io/badge/macOS-13%2B-000000">
  <img alt="No dependencies" src="https://img.shields.io/badge/dependencies-none-success">
  <img alt="MIT" src="https://img.shields.io/badge/license-MIT-blue">
</p>

---

Both systems snap windows to a fixed set of shapes. Zones lets every display
have the layout you want, a wide centre on an ultrawide, a main window and a
stack beside it, two rows on a portrait screen, and keeps more than one size of
it so the middle can be 50% now and 70% a minute later without opening
anything.

## What it does

- **Shift-drag to snap.** Drag any window by its title bar with
  <kbd>Shift</kbd> held. The zones of the display under the cursor appear; let
  go over one and the window fills it. Let go near the line between two zones
  and it spans both. Drag it out again and it gets its own size back.
- **A layout per display.** Eight ready-made ones (halves, thirds, a wide
  centre, two-thirds, a main window and a stack, a grid, six, rows), or draw
  your own: click a zone to split it, drag the line between two zones to
  resize just those two.
- **Sizes.** Keep several versions of a display's layout, the middle at 50%,
  60% and 70% say, and flip between them with a shortcut. Every window snapped
  on that display moves with the zones, sides included. While Shift-dragging,
  the mouse wheel flips sizes before you drop.
- **Keyboard.** Shortcuts move the active window into its next or previous
  zone, round and round, and flip the display to its next or previous size.
  All four can be set to any keys.
- **Native on both.** C# and WinForms on Windows, Swift and AppKit on macOS.
  Light or dark, your accent colour, per-monitor scaling.

## Shortcuts

| | Windows | macOS |
| --- | --- | --- |
| Show the zones while dragging | <kbd>Shift</kbd> | <kbd>⇧</kbd> |
| Active window to its next zone | <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Z</kbd> | <kbd>⌃</kbd><kbd>⌥</kbd><kbd>Z</kbd> |
| Active window to its previous zone | <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>Z</kbd> | <kbd>⌃</kbd><kbd>⌥</kbd><kbd>⇧</kbd><kbd>Z</kbd> |
| Display to its next size | <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>X</kbd> | <kbd>⌃</kbd><kbd>⌥</kbd><kbd>X</kbd> |
| Display to its previous size | <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>X</kbd> | <kbd>⌃</kbd><kbd>⌥</kbd><kbd>⇧</kbd><kbd>X</kbd> |
| Pick a size while dragging | mouse wheel | scroll |

Every shortcut can be changed in the settings window: click it and press the
keys you want, or <kbd>Backspace</kbd> to turn it off.

## The settings window

Open it from the tray icon on Windows or the menu bar icon on macOS.

- **Displays** on the left; pick one and its layout shows on the right, drawn
  to the display's real proportions with each zone's size in pixels.
- **Layouts**: click a ready-made one to start from it.
- **Sizes**: the strip beside "Zones". **+** copies the current size so you can
  drag its lines somewhere else; click a size to switch to it; right-click to
  remove one.
- **Zones**: click to split, <kbd>Shift</kbd>+click to split across, drag a
  line to resize (it clicks into halves, thirds and quarters on the way unless
  you hold <kbd>Alt</kbd>/<kbd>⌥</kbd>), right-click to remove a zone.
- **Show on screen** draws the layout over the real display. The **⋯** menu
  beside it has **Use on all displays**, which copies the layout, sizes and
  all, and **Reset this display**, back to the default.
- **Snapping**: turn snapping off, show zones on every drag instead of only
  with Shift (Shift then holds them back), the gap around each zone, and the
  four shortcuts.

The window can be resized; the zone editor grows with it.

## Install

### Windows

```powershell
git clone https://github.com/rui-branco/zones.git
cd zones\windows
.\install.ps1
```

`install.ps1` builds with the C# compiler that ships with Windows (.NET
Framework 4.8, nothing to install), puts `zones.exe` in `%LOCALAPPDATA%\Zones`,
adds a Start Menu shortcut and starts it. Right-click the tray icon for
**Start with Windows**.

### macOS

```bash
git clone https://github.com/rui-branco/zones.git
cd zones/macos
scripts/install.sh
```

Needs the Xcode command line tools (Swift 5.9 or later). `install.sh` builds
`Zones.app`, puts it in `~/Applications` and opens it. macOS then asks for
**Accessibility** access, which is what lets one app move another's windows:
turn Zones on in System Settings → Privacy & Security → Accessibility. Use the
menu bar icon for **Start at Login**.

The app is signed ad hoc, so macOS treats every rebuild as a new app and asks
for Accessibility again after reinstalling.

## Settings

- Windows: `%APPDATA%\Zones\config.json`, with the previous save kept as
  `config.json.bak`
- macOS: `~/Library/Application Support/Zones/config.json`

Layouts are kept per display (the device name on Windows, the display's UUID
on macOS) in the same JSON shape on both: a tree of runs and zones, each with
its share of the space.

## Project layout

```
windows/   C# 5 / WinForms, built by build.ps1 with the in-box csc.exe
  src/App    startup, config, tray icon
  src/Snap   layout model, drag snapping, overlay, shortcuts, settings window
  src/Ui     theme and the hand-drawn controls
macos/     Swift package
  Sources/ZonesCore   layout model and config (no AppKit, unit tested)
  Sources/Zones       menu bar app: engine, overlay, shortcuts, settings
  scripts/            build-app.sh, install.sh, make-icon.swift
```

Run the Mac core's tests with `swift test` in `macos/`.

## Known limits

- **Windows running as administrator do not snap** on Windows: a normal
  program is not allowed to move an elevated one's windows.
- **Only resizable windows snap.** Fixed-size dialogs and palettes are left
  alone.
- **The system's own snapping still runs.** Dragging to a screen edge can
  still trigger Windows Snap or the macOS tiling bar; Zones wins when you drop
  on one of its zones.
- **Full-screen windows** (macOS) and minimized ones are skipped.

## Licence

MIT. See [LICENSE](LICENSE).
