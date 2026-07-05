using Microsoft.EntityFrameworkCore;
using SitePass.Core.Entities;
using SitePass.Core.Enums;
using SitePass.Infrastructure.Security;
using System.Linq;

namespace SitePass.Infrastructure.Data
{
    public static class DbSeeder
    {
        public static void Seed(SitePassDbContext context)
        {
            context.Database.EnsureCreated();

            if (!context.Users.Any())
            {
                // Default Admin
                var admin = new User
                {
                    FirstName = "Yusuf",
                    LastName = "Yönetici",
                    PhoneNumber = "05551112233",
                    Role = UserRole.Admin,
                    BlockNo = "Yönetim",
                    ApartmentNo = "Ofis"
                };
                admin.PasswordHash = PasswordHasherUtility.HashPassword(admin, "admin123");

                // Default Resident (Sakin)
                var resident = new User
                {
                    FirstName = "Ahmet",
                    LastName = "Sakin",
                    PhoneNumber = "05554445566",
                    Role = UserRole.Resident,
                    BlockNo = "A",
                    ApartmentNo = "12"
                };
                resident.PasswordHash = PasswordHasherUtility.HashPassword(resident, "resident123");

                // Default Security (Güvenlik)
                var security = new User
                {
                    FirstName = "Mehmet",
                    LastName = "Güvenlik",
                    PhoneNumber = "05557778899",
                    Role = UserRole.Security,
                    BlockNo = "Güvenlik",
                    ApartmentNo = "Kulübe"
                };
                security.PasswordHash = PasswordHasherUtility.HashPassword(security, "security123");

                context.Users.AddRange(admin, resident, security);
                context.SaveChanges();

                // Add a permanent vehicle for resident
                var permVehicle = new Vehicle
                {
                    Plate = "34ABC123",
                    ResidentId = resident.Id,
                    IsGuest = false,
                    IsActive = true
                };
                context.Vehicles.Add(permVehicle);
                context.SaveChanges();
            }
        }
    }
}
