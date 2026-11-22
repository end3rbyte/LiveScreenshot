using System.IO;
using System.Windows.Media.Imaging;

namespace LiveScreenshot;

// Writes and removes the fixed PNG.
internal static class SnapshotFile
{
    internal static string DirectoryPath { get; } = GetDirectoryPath();
    internal static string Path { get; } = System.IO.Path.Combine(DirectoryPath, "latest.png");

    // Resolves the output path against the current Windows user profile.
    private static string GetDirectoryPath()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile))
        {
            throw new InvalidOperationException("The Windows user profile folder could not be found.");
        }

        return System.IO.Path.Combine(profile, ".livescreenshot");
    }

    // Encodes the image before overwriting the final PNG directly.
    internal static void Publish(BitmapSource image)
    {
        Directory.CreateDirectory(DirectoryPath);
        using MemoryStream encoded = new();
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(encoded);
        encoded.Position = 0;
        using FileStream output = new(Path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoded.CopyTo(output);
    }

    // Removes the published image.
    internal static void Remove()
    {
        File.Delete(Path);
    }
}
