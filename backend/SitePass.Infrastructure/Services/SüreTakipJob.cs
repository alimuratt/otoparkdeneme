using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SitePass.Infrastructure.Services
{
    public class SüreTakipJob : BackgroundService
    {
        private readonly ExcelManager _excelManager;
        private readonly ILogger<SüreTakipJob> _logger;
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

        public SüreTakipJob(ExcelManager excelManager, ILogger<SüreTakipJob> logger)
        {
            _excelManager = excelManager;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Excel Tabanlı Misafir Araç Süre Takip Robotu (Job) başlatıldı. Periyot: {Interval}", Interval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogDebug("Süre Takip Robotu Excel kontrolü gerçekleştiriyor...");
                    await _excelManager.DeactivateExpiredGuestVehiclesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Excel süre takip robotunda hata oluştu.");
                }

                await Task.Delay(Interval, stoppingToken);
            }

            _logger.LogInformation("Excel Tabanlı Misafir Araç Süre Takip Robotu durduruluyor.");
        }
    }
}
