using System.IO;
using System.Windows.Media.Imaging;

namespace LiveScreenshot;

// Publishes and removes the fixed PNG and its temporary file.
internal static class SnapshotFile
{
    internal static string Path { get; } = GetPath();
    private static string TemporaryPath => Path + ".tmp";

    // Resolves the output path against the current Windows user profile.
    private static string GetPath()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile))
        {
            throw new InvalidOperationException("The Windows user profile folder could not be found.");
        }

        return System.IO.Path.Combine(profile, ".livescreenshot", "latest.png");
    }

    // Publishes a fully encoded image by replacing the file in the same directory.
    internal static void Publish(BitmapSource image)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        using (FileStream output = new(TemporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(output);
        }

        File.Move(TemporaryPath, Path, true);
    }

    // Removes both the published image and any incomplete temporary output.
    internal static void Remove()
    {
        try
        {
            File.Delete(Path);
        }
        finally
        {
            File.Delete(TemporaryPath);
        }
    }
}
