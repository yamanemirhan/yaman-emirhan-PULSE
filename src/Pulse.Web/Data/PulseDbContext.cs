using Microsoft.EntityFrameworkCore;
using Pulse.Web.Domain;

namespace Pulse.Web.Data
{
    public class PulseDbContext(DbContextOptions<PulseDbContext> options) : DbContext(options)
    {
        public DbSet<PulseEvent> Events => Set<PulseEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var events = modelBuilder.Entity<PulseEvent>();

            events.HasKey(e => e.Sequence); // primary key
            events.Property(e => e.Sequence).ValueGeneratedOnAdd();

            events.Property(e => e.Kind).HasConversion<string>();
            events.Property(e => e.ActorRole).HasConversion<string>();

            events.HasIndex(e => new { e.QuestionId, e.Sequence });

            // that means that for a given actor, question and request, there can be only one event
            events.HasIndex(e => new
            {
                e.ActorId,
                e.QuestionId,
                e.RequestId
            }).IsUnique();

            // there can be only ONE QuestionCreated event for a given question
            events.HasIndex(e => e.QuestionId)
                .IsUnique()
                .HasFilter("\"Kind\" = 'QuestionCreated'");
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            RejectHistoryChanges();

            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            RejectHistoryChanges();

            return base.SaveChangesAsync(
                acceptAllChangesOnSuccess,
                cancellationToken);
        }

        private void RejectHistoryChanges()
        {
            ChangeTracker.DetectChanges();

            var historyChanged = ChangeTracker.Entries<PulseEvent>()
                .Any(entry =>
                    entry.State is EntityState.Modified or EntityState.Deleted);

            if (historyChanged)
            {
                throw new InvalidOperationException(
                    "History records cannot be modified or deleted.");
            }
        }
    }
}
