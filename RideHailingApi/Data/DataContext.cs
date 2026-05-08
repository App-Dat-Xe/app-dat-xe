using Microsoft.EntityFrameworkCore;
using RideHailingApi.Models;

namespace RideHailingApi.Data
{
    public class DataContext : DbContext
    {
        public DataContext(DbContextOptions<DataContext> options) : base(options) { }

        public DbSet<ScheduledTrip> ScheduledTrips { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ScheduledTrip>()
                .Property(s => s.EstimatedFare)
                .HasPrecision(18, 2);
        }

        // Keep legacy DataConnect usage for raw SQL operations
    }
}
