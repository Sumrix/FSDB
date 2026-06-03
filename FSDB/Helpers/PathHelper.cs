using System;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.IO;
using System.Security;
using System.Text;
using NeoSmart.Hashing.XXHash;

namespace FSDB.Helpers;

public static class PathHelper
{
    public static readonly StringComparer OSDependedPathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    
    public static string PathId(string path)
    {
        uint h = XXHash32.Hash(Encoding.UTF8.GetBytes(path));

        Span<byte> raw = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(raw, h);

        // 4 bytes -> 8 base64 chars with padding; trim '=' -> 6 chars total.
        Span<byte> b64 = stackalloc byte[8];
        Base64.EncodeToUtf8(raw, b64, out _, out int written);
        string s = Encoding.ASCII.GetString(b64[..written]).TrimEnd('=');

        return s; // 6 chars
    }

    public static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        if (name.IndexOfAny(invalidChars) < 0) return name;

        return string.Create(name.Length, name, (span, original) =>
        {
            for (int i = 0; i < original.Length; i++)
            {
                span[i] = invalidChars.Contains(original[i]) ? '_' : original[i];
            }
        });
    }

    /// <exception cref="ArgumentNullException">The path is null.</exception>
    /// <exception cref="ArgumentException">The system could not retrieve the absolute path.</exception>
    /// <exception cref="SecurityException">The caller does not have the required permissions.</exception>
    /// <exception cref="NotSupportedException">The path contains a format that is not supported.</exception>
    /// <exception cref="PathTooLongException">The specified path, file name, or both exceed the system-defined maximum length.</exception>
    public static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}