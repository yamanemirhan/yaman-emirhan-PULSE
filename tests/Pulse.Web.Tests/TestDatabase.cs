using System.Security.Claims;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Web.Data;
using Pulse.Web.Domain;
using Pulse.Web.Services;

namespace Pulse.Web.Tests;

// A fresh on-disk SQLite database per test, using the application's real migrations.
// Separate connections exercise actual write locking and unique constraints.
public abstract class DatabaseTest : IAsyncLifetime, IDbContextFactory<PulseDbContext>
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pulse-tests", Guid.NewGuid().ToString("N"));
    private DbContextOptions<PulseDbContext> _options = null!;
    protected EventStore Store => new(this, TimeProvider.System);
    protected QuestionService Questions => new(Store, this);
    protected VoteService Votes => new(Store, Questions, this);
    protected EvidenceService Evidence => new(Store, Questions, this);
    protected ResultService Results => new(this, TimeProvider.System);
    protected EventChainVerifier Chain => new(this);
    protected TimelineService Timeline => new(this);
    protected RoomService Room => new(Store, this);
    protected ClaimsPrincipal Admin { get; } = User(PulseRole.Administrator);

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_directory, "test.db"), Pooling = false, DefaultTimeout = 15
        };
        _options = new DbContextOptionsBuilder<PulseDbContext>().UseSqlite(connection.ToString()).Options;
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public PulseDbContext CreateDbContext() => new(_options);

    protected static ClaimsPrincipal User(PulseRole role = PulseRole.Participant, Guid? id = null) =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, (id ?? Guid.NewGuid()).ToString()),
            new Claim(ClaimTypes.Role, role.ToString())
        ], "Test"));

    protected Task<Guid> QuestionAsync(ClaimsPrincipal? owner = null) =>
        Questions.CreateAsync(owner ?? User(), Guid.NewGuid(), Guid.NewGuid(), "Should we run a pilot?");

    protected Task<AppendResult> VoteAsync(ClaimsPrincipal user, Guid question, VoteAnswer answer, int confidence) =>
        Votes.CastAsync(user, question, Guid.NewGuid(), answer, confidence);

    protected async Task<PulseEvent[]> EventsAsync()
    {
        await using var db = CreateDbContext();
        return await db.Events.AsNoTracking().OrderBy(e => e.Sequence).ToArrayAsync();
    }

    public Task DisposeAsync()
    {
        // Delete only files belonging to this test; never the application's App_Data.
        foreach (var name in new[] { "test.db", "test.db-wal", "test.db-shm", "test.db-journal" })
            File.Delete(Path.Combine(_directory, name));
        Directory.Delete(_directory);
        return Task.CompletedTask;
    }
}
