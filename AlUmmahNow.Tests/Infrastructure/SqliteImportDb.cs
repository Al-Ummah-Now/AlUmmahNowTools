using AlUmmahNow.Data.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AlUmmahNow.Tests.Infrastructure;

/// <summary>
/// A file-backed SQLite database with the full <see cref="ImportDbContext"/> model, for tests that need
/// what the in-memory provider lacks: unique and filtered indexes, <c>ExecuteUpdate</c>, and many
/// contexts writing at once (WAL + a busy timeout so parallel writers queue instead of failing).
/// Each <see cref="NewContext"/> is an independent connection, like separate processes or scopes.
/// </summary>
public sealed class SqliteImportDb : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aun-import-{Guid.NewGuid():N}.db");
    private readonly string _connectionString;

    public SqliteImportDb()
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            DefaultTimeout = 60,
            Pooling = false,
        }.ToString();
        using var db = NewContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    /// <summary>Options for this database; every context built from them opens its own connection.</summary>
    public DbContextOptions<ImportDbContext> Options => new DbContextOptionsBuilder<ImportDbContext>()
        .UseSqlite(_connectionString)
        .Options;

    public ImportDbContext NewContext() => new(Options);

    public void Dispose()
    {
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
    }
}
