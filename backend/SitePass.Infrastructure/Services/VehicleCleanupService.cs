using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SitePass.Infrastructure.Data;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SitePass.Infrastructure.Services
{
    public class VehicleCleanupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<VehicleCleanupService> _logger;

        public VehicleCleanupService(IServiceScopeFactory scopeFactory, ILogger<VehicleCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Vehicle Cleanup Background Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DeactivateExpiredGuestVehiclesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred deactivating expired guest vehicles.");
                }

                // Run every minute (60 seconds)
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("Vehicle Cleanup Background Service is stopping.");
        }

        private async Task DeactivateExpiredGuestVehiclesAsync()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<SitePassDbContext>();
                
                var now = DateTime.UtcNow;

                var expiredVehicles = await context.Vehicles
                    .Where(v => v.IsActive && v.IsGuest && v.ExpireDate != null && v.ExpireDate <= now)
                    .ToListAsync();

                if (expiredVehicles.Any())
                {
                    _logger.LogInformation("Found {Count} expired guest vehicles. Deactivating them...", expiredVehicles.Count);

                    foreach (var vehicle in expiredVehicles)
                    {
                        vehicle.IsActive = false;
                    }

                    await context.SaveChangesAsync();
                }
            }
        }
    }
}
