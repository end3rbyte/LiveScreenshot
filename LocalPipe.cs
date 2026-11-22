using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LiveScreenshot;

// Creates a Windows pipe that accepts local clients and rejects remote connections.
internal static partial class LocalPipe
{
    private const uint Duplex = 3;
    private const uint RejectRemoteClients = 8;
    private const uint FirstInstance = 0x00080000;
    private const uint Overlapped = 0x40000000;
    private const int MaximumLineBytes = 64;

    // Grants local clients duplex access, including restricted and low-integrity processes.
    internal static unsafe NamedPipeServerStream CreateServer(string channel, string sid)
    {
        string access = $"O:{sid}D:P(A;;GA;;;{sid})(A;;GRGW;;;WD)(A;;GRGW;;;RC)S:(ML;;NW;;;LW)";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(access, 1, out nint descriptor, 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            SecurityAttributes security = new() { Length = sizeof(SecurityAttributes), Descriptor = descriptor };
            SafePipeHandle handle = CreateNamedPipe($@"\\.\pipe\{channel}", Duplex | Overlapped | FirstInstance,
                RejectRemoteClients, 1, 1024, 1024, 0, in security);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw new Win32Exception(error);
            }

            try
            {
                return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    // Reads one bounded request and returns null if the client disconnects without sending data.
    internal static async Task<string?> ReadRequestAsync(Stream stream, CancellationToken token)
    {
        byte[] line = new byte[MaximumLineBytes];
        for (int index = 0; index < line.Length; index++)
        {
            if (await stream.ReadAsync(line.AsMemory(index, 1), token) == 0)
            {
                return index == 0 ? null : throw new FormatException("The request must end with a newline.");
            }

            if (line[index] == '\n')
            {
                return Encoding.ASCII.GetString(line, 0, index).TrimEnd('\r');
            }
        }

        throw new FormatException("The request is too long.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public nint Descriptor;
        public int InheritHandle;
    }

    // Converts the access rules to a Windows security descriptor.
    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW",
        StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision,
        out nint descriptor, nint size);

    // Creates a duplex pipe with the specified access rule and remote-client policy.
    [LibraryImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafePipeHandle CreateNamedPipe(string name, uint mode, uint pipeMode, uint instances,
        uint outputBuffer, uint inputBuffer, uint timeout, in SecurityAttributes security);

    // Releases the descriptor allocated by Windows.
    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
