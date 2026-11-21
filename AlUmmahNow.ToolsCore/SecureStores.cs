using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlUmmahNow.Core;

namespace AlUmmahNow.ToolsCore;

/// <summary>Where the sign-in tokens live: the OS's protected store, never a plain file.</summary>
public interface ISecureStore
{
    byte[]? Get(string key);
    void Set(string key, byte[] value);
    void Delete(string key);
    string[] List(string prefix);
}

public static class SecureStores
{
    /// <summary>The store of this OS for one environment (each environment keeps its own tokens).</summary>
    public static ISecureStore Create(string storeName)
    {
        if (OperatingSystem.IsWindows())
        {
            return new DpapiSecureStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlUmmahNow", "Tools", storeName));
        }

        if (OperatingSystem.IsMacOS()) return new KeychainSecureStore("net.alummahnow.tools", storeName);
        return new LibSecretStore(storeName);
    }
}

/// <summary>Presents a platform store to aun-core. A locked or missing keyring is a storage error.</summary>
internal sealed class SecureStorageAdapter : SecureStorage
{
    private readonly ISecureStore _store;

    public SecureStorageAdapter(ISecureStore store) => _store = store;

    public byte[]? Get(string key) => Guard(() => _store.Get(key));
    public void Set(string key, byte[] value) => Guard(() => { _store.Set(key, value); return 0; });
    public void Delete(string key) => Guard(() => { _store.Delete(key); return 0; });
    public string[] List(string prefix) => Guard(() => _store.List(prefix));

    private static T Guard<T>(Func<T> f)
    {
        try
        {
            return f();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            throw new AunException.Storage(ex.GetType().Name + ": " + ex.Message);
        }
    }
}

/// <summary>Keys kept in memory: tests and screenshots, never a real sign-in.</summary>
public sealed class MemorySecureStore : ISecureStore
{
    private readonly Dictionary<string, byte[]> _values = new();

    public byte[]? Get(string key)
    {
        lock (_values) return _values.TryGetValue(key, out var v) ? v : null;
    }

    public void Set(string key, byte[] value)
    {
        lock (_values) _values[key] = value;
    }

    public void Delete(string key)
    {
        lock (_values) _values.Remove(key);
    }

    public string[] List(string prefix)
    {
        lock (_values) return _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
    }
}

/// <summary>
/// All keys of one store as a single JSON value (base64 per key), so a platform store that holds one
/// secret per item serves the core's key/value interface. Shared by the Keychain and libsecret stores.
/// </summary>
public abstract class SingleItemSecureStore : ISecureStore
{
    private readonly object _gate = new();

    protected abstract string? ReadItem();
    protected abstract void WriteItem(string value);

    public byte[]? Get(string key)
    {
        lock (_gate) return ReadAll().TryGetValue(key, out var value) ? Convert.FromBase64String(value) : null;
    }

    public void Set(string key, byte[] value)
    {
        lock (_gate)
        {
            var all = ReadAll();
            all[key] = Convert.ToBase64String(value);
            WriteItem(JsonSerializer.Serialize(all));
        }
    }

    public void Delete(string key)
    {
        lock (_gate)
        {
            var all = ReadAll();
            if (all.Remove(key)) WriteItem(JsonSerializer.Serialize(all));
        }
    }

    public string[] List(string prefix)
    {
        lock (_gate) return ReadAll().Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
    }

    private Dictionary<string, string> ReadAll()
    {
        var json = ReadItem();
        if (string.IsNullOrEmpty(json)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (JsonException ex)
        {
            throw new IOException("The stored sign-in is unreadable", ex);
        }
    }
}

/// <summary>Windows: one DPAPI-protected file per key, readable only by the current user. As Desktop's.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecureStore : ISecureStore
{
    private readonly string _directory;

    public DpapiSecureStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public byte[]? Get(string key)
    {
        var path = PathFor(key);
        return File.Exists(path) ? ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser) : null;
    }

    public void Set(string key, byte[] value) =>
        File.WriteAllBytes(PathFor(key), ProtectedData.Protect(value, null, DataProtectionScope.CurrentUser));

    public void Delete(string key) => File.Delete(PathFor(key));

    public string[] List(string prefix) =>
        Directory.EnumerateFiles(_directory, "*.bin")
            .Select(f => Encoding.UTF8.GetString(Convert.FromHexString(Path.GetFileNameWithoutExtension(f))))
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();

    private string PathFor(string key) => Path.Combine(_directory, Convert.ToHexString(Encoding.UTF8.GetBytes(key)) + ".bin");
}

/// <summary>
/// macOS: one generic password in the login Keychain (service <c>net.alummahnow.tools</c>, account
/// the environment's store). Uses the SecKeychain C API, which takes plain buffers.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class KeychainSecureStore : SingleItemSecureStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int ErrSecItemNotFound = -25300;

    private readonly byte[] _service;
    private readonly byte[] _account;

    public KeychainSecureStore(string service, string account)
    {
        _service = Encoding.UTF8.GetBytes(service);
        _account = Encoding.UTF8.GetBytes(account);
    }

    protected override string? ReadItem()
    {
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)_service.Length, _service, (uint)_account.Length, _account, out var length, out var data, out var item);
        if (status == ErrSecItemNotFound) return null;
        Check(status, "read");
        try
        {
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, (int)length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            if (item != IntPtr.Zero) CFRelease(item);
        }
    }

    protected override void WriteItem(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)_service.Length, _service, (uint)_account.Length, _account, out _, out var data, out var item);
        if (status == ErrSecItemNotFound)
        {
            Check(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)_service.Length, _service, (uint)_account.Length, _account, (uint)bytes.Length, bytes, out var added), "add");
            if (added != IntPtr.Zero) CFRelease(added);
            return;
        }

        Check(status, "find");
        try
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)bytes.Length, bytes), "update");
        }
        finally
        {
            if (item != IntPtr.Zero) CFRelease(item);
        }
    }

    private static void Check(int status, string step)
    {
        if (status != 0) throw new IOException("Keychain " + step + " failed (OSStatus " + status + ")");
    }

    [DllImport(Security)] private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName, out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);
    [DllImport(Security)] private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName, uint passwordLength, byte[] passwordData, out IntPtr itemRef);
    [DllImport(Security)] private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);
    [DllImport(Security)] private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);
}

/// <summary>
/// Linux: one libsecret item (GNOME Keyring, KWallet's Secret Service or the Flatpak portal). Calls
/// libsecret directly because its schema API is variadic. As Desktop's, keyed by the store name.
/// </summary>
public sealed class LibSecretStore : SingleItemSecureStore
{
    private const string Glib = "libglib-2.0.so.0";
    private const string Secret = "libsecret-1.so.0";
    private static readonly Lazy<(IntPtr Hash, IntPtr Equal)> GlibFns = new(() =>
    {
        var glib = NativeLibrary.Load(Glib);
        return (NativeLibrary.GetExport(glib, "g_str_hash"), NativeLibrary.GetExport(glib, "g_str_equal"));
    });

    private static readonly Dictionary<string, IntPtr> Interned = new();

    private readonly string _app;
    private IntPtr _schema;

    public LibSecretStore(string app) => _app = app;

    private IntPtr Schema()
    {
        if (_schema != IntPtr.Zero) return _schema;
        var attributes = g_hash_table_new(GlibFns.Value.Hash, GlibFns.Value.Equal);
        g_hash_table_insert(attributes, StaticString("app"), IntPtr.Zero); // SECRET_SCHEMA_ATTRIBUTE_STRING
        _schema = secret_schema_newv("net.alummahnow.Tokens", 0, attributes);
        g_hash_table_unref(attributes);
        return _schema;
    }

    protected override string? ReadItem()
    {
        var attributes = Attributes();
        var result = secret_password_lookupv_sync(Schema(), attributes, IntPtr.Zero, out var error);
        g_hash_table_unref(attributes);
        ThrowIfError(error);
        if (result == IntPtr.Zero) return null;
        var json = Marshal.PtrToStringUTF8(result);
        secret_password_free(result);
        return json;
    }

    protected override void WriteItem(string value)
    {
        var attributes = Attributes();
        var ok = secret_password_storev_sync(Schema(), attributes, IntPtr.Zero, "Al Ummah Now Tools sign-in", value, IntPtr.Zero, out var error);
        g_hash_table_unref(attributes);
        ThrowIfError(error);
        if (!ok) throw new IOException("The keyring refused the item");
    }

    private IntPtr Attributes()
    {
        var table = g_hash_table_new(GlibFns.Value.Hash, GlibFns.Value.Equal);
        g_hash_table_insert(table, StaticString("app"), StaticString(_app));
        return table;
    }

    private static void ThrowIfError(IntPtr error)
    {
        if (error == IntPtr.Zero) return;
        // GError: guint32 domain, gint code, gchar* message
        var message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8));
        g_error_free(error);
        throw new IOException("Keyring error: " + message);
    }

    // Interned for the process lifetime: GHashTable holds these pointers without copying
    private static IntPtr StaticString(string value)
    {
        lock (Interned)
        {
            if (!Interned.TryGetValue(value, out var pointer))
            {
                var bytes = Encoding.UTF8.GetBytes(value + "\0");
                pointer = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
                Interned[value] = pointer;
            }

            return pointer;
        }
    }

    [DllImport(Glib)] private static extern IntPtr g_hash_table_new(IntPtr hash, IntPtr equal);
    [DllImport(Glib)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);
    [DllImport(Glib)] private static extern void g_hash_table_unref(IntPtr table);
    [DllImport(Glib)] private static extern void g_error_free(IntPtr error);
    [DllImport(Secret)] private static extern IntPtr secret_schema_newv([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags, IntPtr attributes);
    [DllImport(Secret)] private static extern IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);
    [DllImport(Secret)] private static extern void secret_password_free(IntPtr password);
    [DllImport(Secret)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool secret_password_storev_sync(IntPtr schema, IntPtr attributes, IntPtr collection, [MarshalAs(UnmanagedType.LPUTF8Str)] string label, [MarshalAs(UnmanagedType.LPUTF8Str)] string password, IntPtr cancellable, out IntPtr error);
}
