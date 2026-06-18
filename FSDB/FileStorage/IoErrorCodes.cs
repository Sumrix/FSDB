using System;
using System.IO;

namespace FSDB.FileStorage;

/// <summary>
/// Provides a heuristic classification of I/O errors based on observed platform-specific error codes.
/// The result is not a reliable truth about the underlying failure and should be treated as an observation.
/// </summary>
internal static class IoErrorCodes
{
    // Windows HRESULT_FROM_WIN32
    private const int HR_SHARING_VIOLATION = unchecked((int)0x80070020);
    private const int HR_LOCK_VIOLATION = unchecked((int)0x80070021);
    private const int HR_SHARING_BUFFER_EXCEEDED = unchecked((int)0x80070024);

    // Unix errno values commonly surfaced in IOException.HResult on Unix.
    // EAGAIN is observed for FileShare conflicts on local Linux.
    // EDEADLK is observed for FileShare conflicts on local macOS.
    // ETXTBSY can surface on some shared/virtual file systems.
    private const int UNIX_EINTR = 4;
    private const int UNIX_EAGAIN = 11;
    private const int UNIX_EBUSY = 16;
    private const int UNIX_ETXTBSY = 26;
    private const int UNIX_EDEADLK = 35;
    private const int UNIX_ESTALE = 116;

    /// <summary>
    /// Determines whether the observed I/O error looks transient on the current platform.
    /// The result is heuristic and should not be treated as a guaranteed property of the failure.
    /// </summary>
    public static bool IsTransient(IOException ex)
    {
        if (OperatingSystem.IsWindows())
            return ex.HResult is HR_SHARING_VIOLATION or HR_LOCK_VIOLATION or HR_SHARING_BUFFER_EXCEEDED;

        // Linux/macOS
        return ex.HResult is UNIX_EINTR or UNIX_EAGAIN or UNIX_EBUSY or UNIX_ETXTBSY or UNIX_EDEADLK or UNIX_ESTALE;
    }

}
