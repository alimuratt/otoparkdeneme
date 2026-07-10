using Microsoft.EntityFrameworkCore;
using SitePass.Core.Entities;

namespace SitePass.Infrastructure.Data
{
    public class OtoparkDbContext : DbContext
    {
        public OtoparkDbContext(DbContextOptions<OtoparkDbContext> options) : base(options)
        {
        }

        public DbSet<BeyazListe> BeyazListe => Set<BeyazListe>();
        public DbSet<GirisCikisLoglari> GirisCikisLoglari => Set<GirisCikisLoglari>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<BeyazListe>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Plaka).IsRequired().HasMaxLength(20);
                entity.HasIndex(e => e.Plaka);
            });

            modelBuilder.Entity<GirisCikisLoglari>(entity =>
            {
                entity.HasKey(e => e.LogId);
                entity.Property(e => e.OkunanPlaka).IsRequired().HasMaxLength(20);
            });
        }
    }
}
