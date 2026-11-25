using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace AlUmmahNow.ToolsCore.Import.Sources;

public enum ReadSystem
{
    Windows,
    MacOS,
    Linux,
}

/// <summary>What a failed read asks for.</summary>
public enum ReadKind
{
    /// <summary>The share stopped answering: wait for it, try the other path, go on from the last good offset</summary>
    Network,
    /// <summary>Another app has the file open, or the disk did not answer: a few tries in place</summary>
    Brief,
    /// <summary>Trying again here and now changes nothing</summary>
    Final,
}

/// <summary>
/// Why a file could not be read: a code for the app's words, what to do about it, and the system's own number and text.
/// Holds no path, so it can be reported to the server.
/// </summary>
/// <param name="Code">share_lost, share_not_found, share_credentials, share_damaged, file_in_use, file_denied, file_missing,
/// disk_read, drive_gone, cloud_offline, name_too_long or file_read</param>
public sealed record ReadFailure(string Code, ReadKind Kind, ReadSystem System, int? Number = null, string Text = "")
{
    public static ReadSystem ThisSystem =>
        OperatingSystem.IsWindows() ? ReadSystem.Windows : OperatingSystem.IsMacOS() ? ReadSystem.MacOS : ReadSystem.Linux;

    /// <summary>Whether the queue may try the step again later by itself.</summary>
    public bool Retryable => Code is not ("file_denied" or "file_missing" or "share_credentials" or "share_damaged" or "name_too_long");

    public string SystemName => System switch { ReadSystem.Windows => "Windows", ReadSystem.MacOS => "macOS", _ => "Linux" };

    /// <summary>"Windows error 59: An unexpected network error occurred." or empty.</summary>
    public string Detail => Number is not { } n ? "" : $"{SystemName} error {n.ToString(CultureInfo.InvariantCulture)}{(Text.Length > 0 ? ": " + Text : "")}";

    /// <summary>In English, for the log, support and the server's error reports.</summary>
    public string Message => Detail.Length > 0 ? $"{Words(Code)} ({Detail})" : Words(Code);

    private static string Words(string code) => code switch
    {
        "share_lost" => "The network share stopped answering while the file was read",
        "share_not_found" => "The network share could not be reached",
        "share_credentials" => "The network share did not accept this computer's sign-in",
        "share_damaged" => "Reading the file stops at the same place every time: the file may be damaged on the server",
        "file_in_use" => "Another app is using the file",
        "file_denied" => "The file may not be read",
        "file_missing" => "The file is no longer in its folder",
        "disk_read" => "The disk could not read the file",
        "drive_gone" => "The drive or network share the file is on is not connected",
        "cloud_offline" => "The file is in cloud storage and could not be downloaded",
        "name_too_long" => "The file's name or path is too long",
        _ => "The file could not be read",
    };

    /// <param name="network">The file is on a network share (an I/O error is then the share's, not a disk's)</param>
    public static ReadFailure Of(Exception error, bool network = false) => Of(error, network, ThisSystem);

    internal static ReadFailure Of(Exception error, bool network, ReadSystem system)
    {
        if (error is SourceReadException known) return known.Failure;
        // No answer at all
        if (error is TimeoutException) return new ReadFailure("share_lost", ReadKind.Network, system);
        var number = NumberOf(error, system);
        var text = number is { } n ? TextOf(n, system) : "";
        ReadFailure As(string code, ReadKind kind) => new(code, kind, system, number, text);

        if (error is FileNotFoundException or DirectoryNotFoundException) return As("file_missing", ReadKind.Final);
        if (error is PathTooLongException) return As("name_too_long", ReadKind.Final);
        if (number is { } code)
        {
            var known2 = system == ReadSystem.Windows ? Windows(code, network) : Unix(code, network, system == ReadSystem.MacOS);
            if (known2 is { } k) return As(k.Code, k.Kind);
        }
        if (error is UnauthorizedAccessException) return As("file_denied", ReadKind.Final);
        return As("file_read", ReadKind.Brief);
    }

    private static (string Code, ReadKind Kind)? Windows(int code, bool network) => code switch
    {
        2 or 3 => ("file_missing", ReadKind.Final),
        // ACCESS_DENIED, NETWORK_ACCESS_DENIED
        5 or 65 => ("file_denied", ReadKind.Final),
        // SHARING_VIOLATION, LOCK_VIOLATION
        32 or 33 => ("file_in_use", ReadKind.Brief),
        // REM_NOT_LIST, BAD_NETPATH, BAD_NET_NAME, NO_NET_OR_BAD_PATH, NO_NETWORK, NETWORK_, HOST_ and PORT_UNREACHABLE, NOT_CONNECTED
        51 or 53 or 67 or 1203 or 1222 or 1231 or 1232 or 1234 or 2250 => ("share_not_found", ReadKind.Network),
        // NETWORK_BUSY, DEV_NOT_EXIST, BAD_NET_RESP, UNEXP_NET_ERR, NETNAME_DELETED, NET_WRITE_FAULT, SEM_TIMEOUT, VC_DISCONNECTED,
        // CONNECTION_REFUSED, GRACEFUL_DISCONNECT, CONNECTION_INVALID, CONNECTION_ABORTED, RETRY, NO_SUCH_LOGON_SESSION, TIMEOUT, and Winsock's
        54 or 55 or 58 or 59 or 64 or 88 or 121 or 240 or 1225 or 1226 or 1229 or 1236 or 1237 or 1312 or 1460 or 10050 or 10051 or 10053 or 10054 or 10060 or 10061 or 10065 => ("share_lost", ReadKind.Network),
        // INVALID_PASSWORD, SESSION_CREDENTIAL_CONFLICT, LOGON_FAILURE, ACCOUNT_RESTRICTION, PASSWORD_EXPIRED, ACCOUNT_DISABLED, LOGON_TYPE_NOT_GRANTED, ACCOUNT_LOCKED_OUT
        86 or 1219 or 1326 or 1327 or 1330 or 1331 or 1385 or 1909 => ("share_credentials", ReadKind.Final),
        // CRC, GEN_FAILURE, DEVICE_HARDWARE_ERROR, IO_DEVICE, FILE_CORRUPT, DISK_CORRUPT
        23 or 31 or 483 or 1117 or 1392 or 1393 => network ? ("share_lost", ReadKind.Network) : ("disk_read", ReadKind.Brief),
        // NOT_READY, NO_SUCH_DEVICE, FILE_INVALID (the volume changed), NO_MEDIA_IN_DRIVE, DEVICE_NOT_CONNECTED, DEVICE_REMOVED
        21 or 433 or 1006 or 1112 or 1167 or 1617 => network ? ("share_lost", ReadKind.Network) : ("drive_gone", ReadKind.Brief),
        // BUFFER_OVERFLOW (the file name is too long), FILENAME_EXCED_RANGE
        111 or 206 => ("name_too_long", ReadKind.Final),
        // ERROR_CLOUD_FILE_*
        358 or (>= 362 and <= 365) or (>= 374 and <= 383) or (>= 386 and <= 398) or 404 or 426 or 434 or 475 => ("cloud_offline", ReadKind.Final),
        _ => null,
    };

    private static (string Code, ReadKind Kind)? Unix(int code, bool network, bool mac)
    {
        // The same on both
        switch (code)
        {
            case 2: return ("file_missing", ReadKind.Final);
            // EPERM, EACCES
            case 1 or 13: return ("file_denied", ReadKind.Final);
            // EIO
            case 5: return network ? ("share_lost", ReadKind.Network) : ("disk_read", ReadKind.Brief);
            // ENXIO, ENODEV
            case 6 or 19: return network ? ("share_lost", ReadKind.Network) : ("drive_gone", ReadKind.Brief);
            // EBUSY, ETXTBSY
            case 16 or 26: return ("file_in_use", ReadKind.Brief);
        }
        if (mac)
        {
            return code switch
            {
                // EDEADLK: a dataless file this process may not download
                11 => ("cloud_offline", ReadKind.Final),
                // EAGAIN: a lock
                35 => ("file_in_use", ReadKind.Brief),
                // ENETDOWN, ENETUNREACH, ENETRESET, ECONNABORTED, ECONNRESET, ENOTCONN, ETIMEDOUT, ECONNREFUSED, EHOSTDOWN, EHOSTUNREACH, ESTALE
                50 or 51 or 52 or 53 or 54 or 57 or 60 or 61 or 64 or 65 or 70 => ("share_lost", ReadKind.Network),
                // ENAMETOOLONG
                63 => ("name_too_long", ReadKind.Final),
                // EAUTH, ENEEDAUTH
                80 or 81 => ("share_credentials", ReadKind.Final),
                _ => null,
            };
        }
        return code switch
        {
            // EAGAIN: a lock
            11 => ("file_in_use", ReadKind.Brief),
            // ENAMETOOLONG
            36 => ("name_too_long", ReadKind.Final),
            // ENETDOWN, ENETUNREACH, ENETRESET, ECONNABORTED, ECONNRESET, ENOTCONN, ETIMEDOUT, ECONNREFUSED, EHOSTDOWN, EHOSTUNREACH, ESTALE, EREMOTEIO
            100 or 101 or 102 or 103 or 104 or 107 or 110 or 111 or 112 or 113 or 116 or 121 => ("share_lost", ReadKind.Network),
            // ENOKEY, EKEYEXPIRED, EKEYREVOKED, EKEYREJECTED
            >= 126 and <= 129 => ("share_credentials", ReadKind.Final),
            _ => null,
        };
    }

    /// <summary>The Win32 error (HResult 0x8007xxxx) or the errno (.NET puts it in HResult as it is), or null.</summary>
    private static int? NumberOf(Exception error, ReadSystem system)
    {
        for (Exception? e = error; e is not null; e = e.InnerException)
        {
            if (e is Win32Exception w) return w.NativeErrorCode;
            if (e is not (IOException or UnauthorizedAccessException)) continue;
            if (system == ReadSystem.Windows)
            {
                if ((e.HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000)) return e.HResult & 0xFFFF;
            }
            else if (e.HResult is > 0 and < 1000)
            {
                return e.HResult;
            }
        }
        return null;
    }

    private static string TextOf(int number, ReadSystem system) =>
        system == ThisSystem ? Marshal.GetPInvokeErrorMessage(number).Trim().TrimEnd('.') : "";
}

/// <summary>
/// A file that could not be read after every try, with the offset the reading stopped at. Its message holds no path.
/// </summary>
public sealed class SourceReadException : IOException
{
    public SourceReadException(ReadFailure failure, long? offset = null, Exception? inner = null)
        : base(offset is { } o ? $"{failure.Message}, at byte {o.ToString(CultureInfo.InvariantCulture)}" : failure.Message, inner)
    {
        Failure = failure;
        Offset = offset;
    }

    public ReadFailure Failure { get; }
    public long? Offset { get; }
}
