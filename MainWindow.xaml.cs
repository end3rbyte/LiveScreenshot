using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Windows.Graphics.Capture;

namespace LiveScreenshot;

// Exposes source selection, capture controls, and capture status.
public partial class MainWindow : Window
{
    private CaptureRun? run;
    private CaptureWindow? captureWindow;
    private PingOverlay? ping;

    // Initializes the capture controls.
    public MainWindow()
    {
        InitializeComponent();
    }

    // Opens a window list that preserves the handle of the chosen capture source.
    private void OpenCaptureWindows(object sender, RoutedEventArgs args)
    {
        ErrorText.Text = "";
        try
        {
            ContextMenu menu = new() { PlacementTarget = CaptureWindowButton, Placement = PlacementMode.Bottom };
            foreach (CaptureWindow window in CaptureWindow.List())
            {
                MenuItem item = new() { Header = window.Title, ToolTip = $"PID {window.ProcessId}, HWND 0x{window.Handle:X}" };
                item.Click += (_, _) => StartWindowCapture(window);
                menu.Items.Add(item);
            }

            if (menu.Items.Count == 0)
            {
                throw new InvalidOperationException("No visible windows are available to capture.");
            }

            menu.IsOpen = true;
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
        }
    }

    // Starts a capture tied to the chosen native window.
    private void StartWindowCapture(CaptureWindow window)
    {
        try
        {
            GraphicsCaptureItem item = window.CreateItem();
            captureWindow = window;
            Start(item);
        }
        catch (Exception error)
        {
            EndCapture(error);
        }
    }

    // Pings a zero-based pixel in the current published screenshot.
    internal void PingCapturedPixel(int x, int y)
    {
        if (run is null || CapturePreview.Source is not BitmapSource image)
        {
            throw new InvalidOperationException("No captured image is available.");
        }

        ShowPing(image, new Point(x, y), new Size(image.PixelWidth, image.PixelHeight));
    }

    // Shows the overlay for a local ping command.
    private void ShowPing(BitmapSource image, Point position, Size coordinateSpace)
    {
        CaptureWindow window = captureWindow ?? throw new InvalidOperationException("No capture window is associated with this image.");
        Point pixel = window.GetScreenPoint(image, position, coordinateSpace);
        ping?.Close();
        PingOverlay overlay = new();
        ping = overlay;
        overlay.Closed += (_, _) => ping = null;
        try
        {
            overlay.ShowAt(pixel);
        }
        catch
        {
            overlay.Close();
            throw;
        }
    }

    // Starts a session and binds its lifecycle callbacks to the UI thread.
    private void Start(GraphicsCaptureItem item)
    {
        ErrorText.Text = "";
        CaptureRun? candidate = null;
        candidate = new CaptureRun(item,
            () => Dispatcher.BeginInvoke(() => EndIfCurrent(candidate!, null)),
            error => Dispatcher.BeginInvoke(() => EndIfCurrent(candidate!, error)),
            (image, error) => Dispatcher.BeginInvoke(() => ShowOutputStatus(candidate!, image, error)));
        run = candidate;
        try
        {
            candidate.Start();
        }
        catch (Exception error)
        {
            EndIfCurrent(candidate, error);
        }

        UpdateControls();
    }

    // Ends only the session that originated the notification.
    private void EndIfCurrent(CaptureRun candidate, Exception? error)
    {
        if (ReferenceEquals(run, candidate))
        {
            EndCapture(error);
        }
    }

    // Displays the latest published image or reports a write error.
    private void ShowOutputStatus(CaptureRun candidate, BitmapSource? image, Exception? error)
    {
        if (ReferenceEquals(run, candidate))
        {
            ErrorText.Text = error?.Message ?? "";
            if (image is not null)
            {
                CapturePreview.Source = image;
            }
        }
    }

    // Stops capture on user request.
    private void StopCapture(object sender, RoutedEventArgs args)
    {
        EndCapture(null);
    }

    // Waits for frame processing to finish, then removes the capture output.
    private void EndCapture(Exception? error)
    {
        CaptureRun? previous = run;
        run = null;
        captureWindow = null;
        ping?.Close();
        CapturePreview.Source = null;
        ErrorText.Text = error?.Message ?? "";
        if (previous is not null)
        {
            try
            {
                try
                {
                    previous.Dispose();
                }
                finally
                {
                    SnapshotFile.Remove();
                }
            }
            catch (Exception cleanupError)
            {
                ErrorText.Text = $"{ErrorText.Text}\n{cleanupError.Message}".Trim();
            }
        }

        UpdateControls();
    }

    // Updates the controls and preview label for the current capture state.
    private void UpdateControls()
    {
        CaptureWindowButton.Visibility = run is null ? Visibility.Visible : Visibility.Collapsed;
        StopButton.Visibility = run is null ? Visibility.Collapsed : Visibility.Visible;
        Status.Text = run is null ? "Stopped" : "Capturing...";
    }

    // Copies the absolute path for use by a local assistant.
    private void CopyPath(object sender, RoutedEventArgs args)
    {
        try
        {
            Clipboard.SetText(SnapshotFile.Path);
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
        }
    }

    // Minimizes the window through the Windows system command.
    private void MinimizeWindow(object sender, ExecutedRoutedEventArgs args)
    {
        SystemCommands.MinimizeWindow(this);
    }

    // Closes the window through the usual capture cleanup path.
    private void CloseWindow(object sender, ExecutedRoutedEventArgs args)
    {
        Close();
    }

    // Restores the window without changing the current capture.
    internal void RequestActivation()
    {
        if (WindowState == WindowState.Minimized)
        {
            SystemCommands.RestoreWindow(this);
        }

        Activate();
    }

    // Stops capture before the main window closes.
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        if (run is not null)
        {
            EndCapture(null);
            if (ErrorText.Text.Length != 0)
            {
                MessageBox.Show(this, ErrorText.Text, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // Shuts down the process after capture cleanup.
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current.Shutdown();
    }
}
