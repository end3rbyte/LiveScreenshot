# LiveScreenshot

Share a window or your entire desktop with your AI assistant. During capture, LiveScreenshot updates the same PNG on disk every second. The file stays at a fixed path in your Windows user profile (`%USERPROFILE%\.livescreenshot\latest.png`), so an assistant with access to local files can always use the same path.

## Get started

1. Download [LiveScreenshot for Windows x64](https://github.com/end3rbyte/LiveScreenshot/releases/tag/v1.0.0). It requires the [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Open LiveScreenshot, click **Open Window Picker**, and select a window or an entire display.
3. Click **Copy path**, then give that path to your AI assistant and ask it to look at your screen.

## Set up your assistant

Add this instruction to your `AGENTS.md`.

```text
When I say “look at my screen” or use an equivalent phrase in any language, open %USERPROFILE%\.livescreenshot\latest.png.
If the file is missing, tell me that no LiveScreenshot capture is available.
You can also consult this file whenever seeing my screen would help with the task.
```
