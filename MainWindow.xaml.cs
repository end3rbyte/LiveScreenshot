using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Windows.Graphics.Capture;
using WinRT.Interop;

namespace LiveScreenshot;

// Exposes source selection, capture controls, and capture status.
public partial class MainWindow : Window
{
    private CaptureRun? run;
    private bool picking;
    private bool closing;

    // Initializes the capture controls.
    public MainWindow()
    {
        InitializeComponent();
    }

    // Opens the system picker owned by the WPF window.
    private async void PickWindow(object sender, RoutedEventArgs args)
    {
        picking = true;
        UpdateControls();
        ErrorText.Text = "";
        try
        {
            GraphicsCaptureItem? item = await PickOtherSourceAsync();
            if (item is not null && !closing)
            {
                Start(item);
            }
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
        }
        finally
        {
            picking = false;
            UpdateControls();
        }
    }

    // Excludes this window while the native picker is open.
    private async Task<GraphicsCaptureItem?> PickOtherSourceAsync()
    {
        nint window = new WindowInteropHelper(this).Handle;
        CaptureInterop.SetCaptureExclusion(window, true);
        try
        {
            GraphicsCapturePicker picker = new();
            InitializeWithWindow.Initialize(picker, window);
            return await picker.PickSingleItemAsync();
        }
        finally
        {
            if (!closing)
            {
                CaptureInterop.SetCaptureExclusion(window, false);
            }
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
        WindowButton.IsEnabled = !picking && run is null;
        WindowButton.Visibility = run is null ? Visibility.Visible : Visibility.Collapsed;
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

        closing = true;
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
