using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Windows;

namespace LiveScreenshot;

// Owns startup, shutdown, and activation of the running instance.
public partial class App : Application
{
    private const string ChannelPrefix = "LiveScreenshot-";
    private const string CommandChannel = "LiveScreenshot-Command";
    private Mutex? instance;
    private bool ownsInstance;
    private readonly CancellationTokenSource shutdown = new();

    // Starts the application or signals the running instance.
    protected override async void OnStartup(StartupEventArgs e)
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        Thread.CurrentThread.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture;
        base.OnStartup(e);
        if (e.Args.Length != 0)
        {
            Console.Error.WriteLine("LiveScreenshot does not accept command-line arguments.");
            Shutdown(1);
            return;
        }

        try
        {
            PingScript.InstallIfMissing(SnapshotFile.DirectoryPath);
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            string sid = identity.User?.Value ?? throw new InvalidOperationException("The Windows user identity could not be found.");
            string channel = ChannelPrefix + sid;
            instance = new Mutex(true, $@"Global\{channel}", out ownsInstance);
            if (!ownsInstance)
            {
                using NamedPipeClientStream client = new(".", channel, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(3000);
                Shutdown();
                return;
            }

            MainWindow window = new();
            MainWindow = window;
            window.Show();
            await Task.WhenAll(ListenForActivationAsync(channel, window), ListenForPingsAsync(sid, window));
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "LiveScreenshot", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    // Accepts second launches from the same Windows account.
    private async Task ListenForActivationAsync(string channel, MainWindow window)
    {
        try
        {
            using NamedPipeServerStream server = new(channel, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            while (!shutdown.IsCancellationRequested)
            {
                await server.WaitForConnectionAsync(shutdown.Token);
                using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    // Let the client verify pipe ownership and close its connection first.
                    if (await server.ReadAtLeastAsync(new byte[1], 1, false, deadline.Token) == 0)
                    {
                        window.RequestActivation();
                    }
                }
                catch (OperationCanceledException) when (!shutdown.IsCancellationRequested)
                {
                    // Release clients that never close their activation connection.
                }
                catch (IOException)
                {
                    // A disconnected client must not stop the listener.
                }
                finally
                {
                    server.Disconnect();
                }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return;
        }
        catch (IOException error)
        {
            MessageBox.Show(window, $"The activation channel could not run. {error.Message}", "LiveScreenshot",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Retains ownership of the fixed local ping channel while the app is open.
    private async Task ListenForPingsAsync(string sid, MainWindow window)
    {
        try
        {
            using NamedPipeServerStream server = LocalPipe.CreateServer(CommandChannel, sid);
            while (!shutdown.IsCancellationRequested)
            {
                await server.WaitForConnectionAsync(shutdown.Token);
                try
                {
                    await HandleRequestAsync(server, window);
                }
                catch (IOException)
                {
                    // A disconnected client must not stop the listener.
                }
                finally
                {
                    server.Disconnect();
                }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error) when (!shutdown.IsCancellationRequested)
        {
            MessageBox.Show(window, $"The local command channel is unavailable. {error.Message}", "LiveScreenshot",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Handles one request on the UI dispatcher and replies to the caller.
    private async Task HandleRequestAsync(NamedPipeServerStream server, MainWindow window)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        string response;
        try
        {
            string? request = await LocalPipe.ReadRequestAsync(server, deadline.Token);
            shutdown.Token.ThrowIfCancellationRequested();
            if (request is null)
            {
                return;
            }

            (int x, int y) = ParsePing(request.Split(' '));
            window.PingCapturedPixel(x, y);
            response = "OK";
        }
        catch (Exception error) when (error is not IOException && !shutdown.IsCancellationRequested)
        {
            response = $"ERROR {error.Message.Replace('\r', ' ').Replace('\n', ' ')}";
        }

        await server.WriteAsync(Encoding.UTF8.GetBytes(response + "\n"), shutdown.Token);
        using CancellationTokenSource replyDeadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        replyDeadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            // Disconnect discards unread bytes, so let the client read and close first.
            await server.ReadAtLeastAsync(new byte[1], 1, false, replyDeadline.Token);
        }
        catch (OperationCanceledException) when (!shutdown.IsCancellationRequested)
        {
            // A client that stays connected must not block other requests indefinitely.
        }
    }

    // Validates the command name and its two nonnegative pixel coordinates.
    private static (int X, int Y) ParsePing(string[] args)
    {
        if (args.Length != 3 || !args[0].Equals("ping", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int x) ||
            !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int y))
        {
            throw new FormatException("Expected PING X Y with nonnegative integer pixel coordinates.");
        }

        return (x, y);
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
