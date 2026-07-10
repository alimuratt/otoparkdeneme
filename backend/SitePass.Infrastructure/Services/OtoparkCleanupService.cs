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
    public class OtoparkCleanupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OtoparkCleanupService> _logger;

        // TEST NOTU: Evde testleri hızlı yapabilmek amacıyla Job'ın çalışma periyodunu buradan ayarlayabilirsiniz.
        // Varsayılan: 10 saniye. 
        // Gerçek sistemde saatlik veya günlük çalışabilir (Örn: TimeSpan.FromHours(1)).
        private static readonly TimeSpan RunInterval = TimeSpan.FromSeconds(10);

        public OtoparkCleanupService(IServiceScopeFactory scopeFactory, ILogger<OtoparkCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Otopark Beyaz Liste Temizlik Robotu (Job) başlatıldı. Çalışma Aralığı: {Interval}", RunInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DeactivateExpiredGuestVehiclesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Otopark temizlik robotunda hata oluştu.");
                }

                await Task.Delay(RunInterval, stoppingToken);
            }

            _logger.LogInformation("Otopark Beyaz Liste Temizlik Robotu durduruluyor.");
        }

        private async Task DeactivateExpiredGuestVehiclesAsync()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var otoparkDb = scope.ServiceProvider.GetService<OtoparkDbContext>();
                
                // OtoparkDbContext kaydedilmemişse veya veritabanı yoksa işlem yapma (hata vermemesi için güvenli kontrol)
                if (otoparkDb == null)
                {
                    return;
                }

                var now = DateTime.Now; // Not: SQL Server local saatine göre DateTime.Now kullanıyoruz.

                var expiredGuests = await otoparkDb.BeyazListe
                    .Where(b => b.IsGuest && b.IsActive && b.ExpireDate != null && b.ExpireDate <= now)
                    .ToListAsync();

                if (expiredGuests.Any())
                {
                    _logger.LogInformation("MevcutOtoparkSistemi DB: Süresi dolmuş {Count} adet misafir araç bulundu. Pasifleştiriliyor...", expiredGuests.Count);

                    foreach (var vehicle in expiredGuests)
                    {
                        vehicle.IsActive = false;
                        _logger.LogInformation("Pasif yapılan plaka: {Plate}, Süre Bitiş: {ExpireDate}", vehicle.Plaka, vehicle.ExpireDate);
                    }

                    await otoparkDb.SaveChangesAsync();
                }
            }
        }
    }
}
