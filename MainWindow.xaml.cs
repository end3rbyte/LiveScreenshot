using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using Windows.Graphics.Capture;
using WinRT.Interop;

namespace LiveScreenshot;

// Exposes source selection, capture controls, and capture status.
public partial class MainWindow : Window
{
    private readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CaptureRun? run;
    private bool picking;
    private bool closing;

    // Initializes the capture controls and status timer.
    public MainWindow()
    {
        InitializeComponent();
        OutputPath.Text = SnapshotFile.Path;
        statusTimer.Tick += RefreshStatus;
    }

    // Opens the system picker owned by the WPF window.
    private async void PickWindow(object sender, RoutedEventArgs args)
    {
        picking = true;
        UpdateControls();
        ErrorText.Text = "";
        try
        {
            GraphicsCapturePicker picker = new();
            InitializeWithWindow.Initialize(picker, new WindowInteropHelper(this).Handle);
            GraphicsCaptureItem? item = await picker.PickSingleItemAsync();
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

    // Starts a session and binds its lifecycle callbacks to the UI thread.
    private void Start(GraphicsCaptureItem item)
    {
        ErrorText.Text = "";
        CaptureRun? candidate = null;
        candidate = new CaptureRun(item,
            () => Dispatcher.BeginInvoke(() => EndIfCurrent(candidate!, null)),
            error => Dispatcher.BeginInvoke(() => EndIfCurrent(candidate!, error)),
            error => Dispatcher.BeginInvoke(() => ShowOutputStatus(candidate!, error)));
        run = candidate;
        try
        {
            candidate.Start();
            Status.Text = $"Starting capture · {candidate.Name}";
            statusTimer.Start();
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

    // Displays the latest write result while the originating session is active.
    private void ShowOutputStatus(CaptureRun candidate, Exception? error)
    {
        if (ReferenceEquals(run, candidate))
        {
            ErrorText.Text = error?.Message ?? "";
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
        statusTimer.Stop();
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

        Status.Text = "Idle";
        UpdateControls();
    }

    // Displays whether the selected source is delivering frames.
    private void RefreshStatus(object? sender, EventArgs args)
    {
        if (run is not null)
        {
            Status.Text = run.ReceivingFrames
                ? $"Capturing · {run.Name}"
                : $"Waiting for a frame · {run.Name}";
        }
    }

    // Enables source selection only while idle.
    private void UpdateControls()
    {
        WindowButton.IsEnabled = !picking && run is null;
        StopButton.IsEnabled = run is not null;
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
