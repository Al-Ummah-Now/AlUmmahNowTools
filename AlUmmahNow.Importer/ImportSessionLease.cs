using System;
using System.Threading;
using System.Threading.Tasks;
using AlUmmahNow.Data.Database;
using AlUmmahNow.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AlUmmahNow.Importer
{
    /// <summary>Another run is already importing this folder or session.</summary>
    public sealed class ImportAlreadyRunningException : InvalidOperationException
    {
        public ImportAlreadyRunningException(string message) : base(message) { }
    }

    /// <summary>This run's session was taken over (its heartbeat lapsed and another run replaced it).</summary>
    public sealed class ImportLeaseLostException : InvalidOperationException
    {
        public ImportLeaseLostException(Guid sessionId)
            : base($"Import session {sessionId} was taken over by another run; stopping.") { }
    }

    /// <summary>
    /// Exclusive ownership of an <see cref="ImportStatus.InProgress"/> session.
    /// </summary>
    /// <remarks>
    /// One running session per root folder is enforced by a filtered unique index (see
    /// <see cref="ImportDbSchema.ImportSessionsOneRunningPerRootIndex"/>), so two runs started at the same time
    /// can't both get one. The owner refreshes <see cref="ImportSession.HeartbeatAt"/> on its own connection;
    /// a session whose heartbeat is older than the lease (the process crashed or was killed) is treated as
    /// interrupted, so a new import of the folder or a Resume can take over instead of being blocked forever.
    /// </remarks>
    internal sealed class ImportSessionLease : IAsyncDisposable
    {
        private readonly DbContextOptions<ImportDbContext> _options;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _heartbeat;
        private volatile bool _lost;

        public Guid SessionId { get; }

        private ImportSessionLease(DbContextOptions<ImportDbContext> options, Guid sessionId, TimeSpan lease)
        {
            _options = options;
            SessionId = sessionId;
            var interval = TimeSpan.FromTicks(Math.Max(lease.Ticks / 4, TimeSpan.FromMilliseconds(100).Ticks));
            _heartbeat = Task.Run(() => HeartbeatLoopAsync(interval));
        }

        /// <summary>Throws if another run has taken this session over.</summary>
        public void ThrowIfLost()
        {
            if (_lost) throw new ImportLeaseLostException(SessionId);
        }

        /// <summary>Creates a new InProgress session for <paramref name="rootFolder"/>, or throws if one is running.</summary>
        public static async Task<(ImportSession Session, ImportSessionLease Lease)> StartNewAsync(
            ImportDbContext db, string rootFolder, string userId, TimeSpan lease)
        {
            var hash = PathHash.OfFolder(rootFolder);
            var now = DateTime.UtcNow;
            await ExpireStaleAsync(db, hash, now - lease, now);

            var session = new ImportSession
            {
                RootFolder = rootFolder,
                UserId = userId,
                Status = ImportStatus.InProgress,
                HeartbeatAt = now,
            };
            db.ImportSessions.Add(session);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                db.Entry(session).State = EntityState.Detached;
                if (await AnotherRunningAsync(db, hash, session.Id))
                {
                    throw new ImportAlreadyRunningException($"An import of '{rootFolder}' is already running.");
                }
                throw;
            }
            return (session, new ImportSessionLease(OptionsOf(db), session.Id, lease));
        }

        /// <summary>
        /// Takes over an existing session for Resume: allowed when it isn't Completed and isn't held by a live
        /// run. Rows a crashed run left in Uploading go back to Pending. Throws if another run holds it or another
        /// session for the same root is running.
        /// </summary>
        public static async Task<ImportSessionLease> ResumeAsync(ImportDbContext db, ImportSession session, TimeSpan lease)
        {
            var hash = PathHash.OfFolder(session.RootFolder);
            var now = DateTime.UtcNow;
            var cutoff = now - lease;
            await ExpireStaleAsync(db, hash, cutoff, now, exceptId: session.Id);

            int claimed;
            try
            {
                claimed = await db.ImportSessions
                    .Where(s => s.Id == session.Id
                        && s.Status != ImportStatus.Completed
                        && (s.Status != ImportStatus.InProgress || s.HeartbeatAt == null || s.HeartbeatAt < cutoff))
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(s => s.Status, ImportStatus.InProgress)
                        .SetProperty(s => s.HeartbeatAt, now)
                        .SetProperty(s => s.RootFolderHash, hash)
                        .SetProperty(s => s.ErrorMessage, (string?)null)
                        .SetProperty(s => s.EndTime, (DateTime?)null));
            }
            catch (Exception)
            {
                if (await AnotherRunningAsync(db, hash, session.Id))
                {
                    throw new ImportAlreadyRunningException($"Another import of '{session.RootFolder}' is running.");
                }
                throw;
            }
            if (claimed == 0)
            {
                throw new ImportAlreadyRunningException($"Import session {session.Id} is already running (or completed).");
            }

            // We own the session exclusively now, so nothing else is uploading its rows.
            await db.ImportVideos
                .Where(v => v.SessionId == session.Id && v.Status == VideoImportStatus.Uploading)
                .ExecuteUpdateAsync(u => u.SetProperty(v => v.Status, VideoImportStatus.Pending));

            await db.Entry(session).ReloadAsync();
            return new ImportSessionLease(OptionsOf(db), session.Id, lease);
        }

        private static Task<int> ExpireStaleAsync(ImportDbContext db, string hash, DateTime cutoff, DateTime now, Guid? exceptId = null)
            => db.ImportSessions
                .Where(s => s.RootFolderHash == hash
                    && s.Status == ImportStatus.InProgress
                    && (s.HeartbeatAt == null || s.HeartbeatAt < cutoff)
                    && (exceptId == null || s.Id != exceptId))
                .ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.Status, ImportStatus.Failed)
                    .SetProperty(s => s.ErrorMessage, "Interrupted: the importing process stopped reporting progress.")
                    .SetProperty(s => s.EndTime, now));

        private static Task<bool> AnotherRunningAsync(ImportDbContext db, string hash, Guid self)
            => db.ImportSessions.AsNoTracking().AnyAsync(s => s.RootFolderHash == hash && s.Status == ImportStatus.InProgress && s.Id != self);

        private static DbContextOptions<ImportDbContext> OptionsOf(ImportDbContext db)
            => (DbContextOptions<ImportDbContext>)db.GetService<IDbContextOptions>();

        private async Task HeartbeatLoopAsync(TimeSpan interval)
        {
            using var timer = new PeriodicTimer(interval);
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token))
                {
                    try
                    {
                        // Own context: the importer's context is busy on the import thread.
                        await using var db = new ImportDbContext(_options);
                        var now = DateTime.UtcNow;
                        var alive = await db.ImportSessions
                            .Where(s => s.Id == SessionId && s.Status == ImportStatus.InProgress)
                            .ExecuteUpdateAsync(u => u.SetProperty(s => s.HeartbeatAt, now), _stop.Token);
                        if (alive == 0)
                        {
                            _lost = true;
                            return;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception)
                    {
                        // Transient database error: try again next tick. If it persists past the lease,
                        // another run may take over and the next successful tick reports the loss.
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await _heartbeat;
            _stop.Dispose();
        }
    }
}
