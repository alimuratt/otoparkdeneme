using Microsoft.EntityFrameworkCore;
using SitePass.Core.Entities;
using SitePass.Core.Enums;
using System;

namespace SitePass.Infrastructure.Data
{
    public class SitePassDbContext : DbContext
    {
        public SitePassDbContext(DbContextOptions<SitePassDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Vehicle> Vehicles => Set<Vehicle>();
        public DbSet<Delivery> Deliveries => Set<Delivery>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("Users");

                entity.HasKey(u => u.Id);
                entity.Property(u => u.FirstName).IsRequired().HasMaxLength(50);
                entity.Property(u => u.LastName).IsRequired().HasMaxLength(50);
                entity.Property(u => u.BlockNo).HasMaxLength(20);
                entity.Property(u => u.ApartmentNo).HasMaxLength(20);
                entity.Property(u => u.PhoneNumber).IsRequired().HasMaxLength(20);
                entity.Property(u => u.PasswordHash).IsRequired().HasMaxLength(255);
                
                // Store Role as string for database readability, or integer
                entity.Property(u => u.Role)
                    .HasConversion(
                        v => v.ToString(),
                        v => (UserRole)Enum.Parse(typeof(UserRole), v))
                    .HasMaxLength(20)
                    .IsRequired();

                // Unique index for phone number
                entity.HasIndex(u => u.PhoneNumber).IsUnique();
            });

            // Vehicle configuration
            modelBuilder.Entity<Vehicle>(entity =>
            {
                entity.ToTable("Vehicles");

                entity.HasKey(v => v.Id);
                entity.Property(v => v.Plate).IsRequired().HasMaxLength(20);
                entity.Property(v => v.IsGuest).IsRequired();
                entity.Property(v => v.CreatedDate).IsRequired();
                entity.Property(v => v.IsActive).IsRequired();

                // Unique index for Plate (plaka benzersiz)
                entity.HasIndex(v => v.Plate).IsUnique();

                // Relationship: User (Resident) -> Vehicles
                entity.HasOne(v => v.Resident)
                    .WithMany(u => u.Vehicles)
                    .HasForeignKey(v => v.ResidentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Delivery configuration
            modelBuilder.Entity<Delivery>(entity =>
            {
                entity.ToTable("Deliveries");

                entity.HasKey(d => d.Id);
                entity.Property(d => d.DeliveryType).IsRequired().HasMaxLength(50);
                entity.Property(d => d.IsActive).IsRequired();
                entity.Property(d => d.CreatedDate).IsRequired();
                entity.Property(d => d.ExpireDate).IsRequired();
                entity.Property(d => d.IsProcessed).IsRequired();

                // Relationship: User (Resident) -> Deliveries
                entity.HasOne(d => d.Resident)
                    .WithMany(u => u.Deliveries)
                    .HasForeignKey(d => d.ResidentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
