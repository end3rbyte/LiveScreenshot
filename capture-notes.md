# LiveScreenshot

Share the latest view of a selected window or display with Codex or another AI assistant that can read local files. Give it the fixed PNG path and ask it to inspect the image on demand, for example to identify which button to click in Blender.

- English-language Windows desktop app in C# with WPF on .NET. Use dark mode. One instance only. A second launch exits after asking the running instance to restore and focus its window if Windows allows it. Capture continues.
- **Open Window Picker** opens the native picker. Confirming a source starts capture. Canceling leaves the app idle.
- The **Copy path** icon copies the absolute Windows path to `latest.png` to the clipboard.
- Keep the native Windows border visible during capture with [IsBorderRequired](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired) set to true.
- Every second, replace the same PNG with the latest valid frame. Keep no history. In window mode, pause capture while the selected window is minimized or not visible and resume when it is visible again. Show the paused status and leave `latest.png` readable and unchanged. Delete it only when the capture session ends.
- **Stop Capture** is enabled during capture. Stop automatically if the selected window closes, its app exits, or the capture app closes. Whenever capture ends for any reason, finish any pending write, then delete the PNG.

## Implementation and checks

- Use Windows.Graphics.Capture with Direct3D 11 and pass the WPF window handle to the picker. [Win32CaptureSample](https://github.com/robmikh/Win32CaptureSample)
- Use `~/.livescreenshot/latest.png` as the fixed output path, relative to the Windows user profile. Write to `~/.livescreenshot/latest.png.tmp`, then replace `latest.png` once the write completes. Report file errors and preserve the last valid image if an update fails.
- Verify window and display selection, picker cancellation, pause and resume when the target is minimized, hidden, or fully covered, border removal and PNG deletion on manual stop, window closure, and target app exit, stopping during a write, concurrent reads, and simultaneous launches.
