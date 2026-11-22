using System.IO;

namespace LiveScreenshot;

// Installs the bundled ping client in the user's LiveScreenshot folder.
internal static class PingScript
{
    // Publishes a complete script while preserving any existing copy.
    internal static void InstallIfMissing(string directory)
    {
        string destination = Path.Combine(directory, "ping.py");
        if (File.Exists(destination))
        {
            return;
        }

        using Stream script = typeof(PingScript).Assembly.GetManifestResourceStream("LiveScreenshot.ping.py")
            ?? throw new InvalidOperationException("The bundled ping.py script is missing.");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".ping-{Guid.NewGuid():N}.tmp");
        FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            using (output)
            {
                script.CopyTo(output);
            }

            File.Move(temporary, destination);
        }
        catch (IOException error) when (error.HResult is unchecked((int)0x80070050) or unchecked((int)0x800700B7)
            && File.Exists(destination))
        {
            // Another launch installed the script first.
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
