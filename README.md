# LiveScreenshot

- Share your screen or window with your AI assistant.
- Ask it to point out things on your screen.

## Get started

1. Install the [.NET 10 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Download and open [LiveScreenshot for Windows x64](https://github.com/end3rbyte/LiveScreenshot/releases/tag/v1.0.0).
3. Click **Open Window Picker** and select the window you want to share.

## To add in your AGENTS.md

Replace `C:/Users/<your-windows-user>` with the absolute path to your Windows profile before copying the instructions into your local or global `AGENTS.md`.

```text
# LiveScreenshot

- When I ask you to look at my screen in any language, call `view_image` directly with `C:/Users/<your-windows-user>/.livescreenshot/latest.png`.
  Use the same call whenever a screenshot would help with the task.
- If the image tool reports that the file is missing, tell me that no LiveScreenshot capture is available.
  Report other read errors as given.
- When I ask you to point at something, open the latest PNG and locate the target using zero-based pixel coordinates from the top-left corner of the original image.
- Run `python C:/Users/<your-windows-user>/.livescreenshot/ping.py X Y` directly, passing the script path, X, and Y as separate arguments.
  Wait for the script to exit.
- Confirm the ping only if the script prints `OK` and exits with code 0. Otherwise, report the error.
```
