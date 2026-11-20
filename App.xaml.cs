using System.Globalization;
using System.IO.Pipes;
using System.Security.Principal;
using System.Windows;

namespace LiveScreenshot;

// Owns startup, shutdown, and activation of the running instance.
public partial class App : Application
{
    private Mutex? instance;
    private bool ownsInstance;
    private readonly CancellationTokenSource shutdown = new();

    // Starts the application or signals the running instance.
    protected override async void OnStartup(StartupEventArgs e)
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        Thread.CurrentThread.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture;
        base.OnStartup(e);
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            string sid = identity.User?.Value ?? throw new InvalidOperationException("The Windows user identity could not be found.");
            string channel = $"LiveScreenshot-{sid}";
            instance = new Mutex(true, $@"Global\{channel}", out ownsInstance);
            if (!ownsInstance)
            {
                using NamedPipeClientStream client = new(".", channel, PipeDirection.Out);
                await client.ConnectAsync(3000);
                Shutdown();
                return;
            }

            MainWindow window = new();
            MainWindow = window;
            window.Show();
            await ListenAsync(channel, window);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "LiveScreenshot", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    // Accepts same-user activation requests until shutdown.
    private async Task ListenAsync(string channel, MainWindow window)
    {
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                using NamedPipeServerStream server = new(channel, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(shutdown.Token);
                if (!shutdown.IsCancellationRequested)
                {
                    window.RequestActivation();
                }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return;
        }
    }

    // Cancels activation and releases the instance mutex on its owning thread.
    protected override void OnExit(ExitEventArgs e)
    {
        shutdown.Cancel();
        if (ownsInstance)
        {
            instance?.ReleaseMutex();
        }

        instance?.Dispose();
        base.OnExit(e);
    }
}
