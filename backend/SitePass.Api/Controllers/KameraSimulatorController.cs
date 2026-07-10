using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SitePass.Api.Hubs;
using SitePass.Core.Entities;
using SitePass.Infrastructure.Data;
using SitePass.Infrastructure.Services;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace SitePass.Api.Controllers
{
    [ApiController]
    [Route("api/test")]
    public class KameraSimulatorController : ControllerBase
    {
        private readonly SitePassDbContext _sitePassContext;
        private readonly ExcelManager _excelManager;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly PushNotificationService _pushService;

        public KameraSimulatorController(
            SitePassDbContext sitePassContext,
            ExcelManager excelManager,
            IHubContext<NotificationHub> hubContext,
            PushNotificationService pushService)
        {
            _sitePassContext = sitePassContext;
            _excelManager = excelManager;
            _hubContext = hubContext;
            _pushService = pushService;
        }

        public class KameraOkuRequest
        {
            public string Plate { get; set; } = string.Empty;
            public string Plaka { get; set; } = string.Empty;
        }

        [HttpPost("kamera-oku")]
        public async Task<IActionResult> KameraOku([FromBody] KameraOkuRequest request)
        {
            var rawPlate = !string.IsNullOrEmpty(request.Plaka) ? request.Plaka : request.Plate;
            var cleanPlate = NormalizePlate(rawPlate);

            if (string.IsNullOrEmpty(cleanPlate))
            {
                return BadRequest(new { Success = false, Message = "Lütfen geçerli bir plaka belirtin." });
            }

            var now = DateTime.Now;

            // 1. Excel'den BeyazListe'yi oku ve plakayı bul
            var beyazListe = await _excelManager.GetBeyazListeAsync();
            var beyazListeKaydi = beyazListe
                .FirstOrDefault(b => b.Plaka == cleanPlate && b.IsActive && (b.ExpireDate == null || b.ExpireDate > now));

            if (beyazListeKaydi == null)
            {
                return Ok(new
                {
                    BariyerAcildi = false,
                    Success = false,
                    Message = $"Plaka ({cleanPlate}) Beyaz Liste'de bulunamadı veya pasif durumda! Bariyer açılmadı."
                });
            }

            // 2. Excel'deki GirisCikisLoglari sayfasına yeni log ekle
            var log = new GirisCikisLoglari
            {
                OkunanPlaka = cleanPlate,
                GirisTarihi = DateTime.Now,
                KameraKodu = "KAPI_1_GIRIS"
            };
            await _excelManager.AddGirisCikisLogAsync(log);

            // 3. Eğer misafir araç ise ilgili sakine SignalR üzerinden bildirim tetikle
            string residentName = beyazListeKaydi.SahipAdSoyad;
            string messageStatus = "Bariyer Açıldı.";

            if (beyazListeKaydi.IsGuest)
            {
                // Kendi veritabanımızdan sakini bulalım
                var sitePassVehicle = await _sitePassContext.Vehicles
                    .Include(v => v.Resident)
                    .FirstOrDefaultAsync(v => v.Plate == cleanPlate && v.IsActive);

                User? targetResident = sitePassVehicle?.Resident;

                if (targetResident == null)
                {
                    targetResident = await _sitePassContext.Users
                        .FirstOrDefaultAsync(u => (u.FirstName + " " + u.LastName).ToLower() == beyazListeKaydi.SahipAdSoyad.ToLower());
                }

                if (targetResident == null && !string.IsNullOrEmpty(beyazListeKaydi.BlokDaire))
                {
                    var parts = beyazListeKaydi.BlokDaire.Split(new[] { '-', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var block = parts[0];
                        var apartment = parts[1];
                        targetResident = await _sitePassContext.Users
                            .FirstOrDefaultAsync(u => u.BlockNo == block && u.ApartmentNo == apartment);
                    }
                }

                if (targetResident != null)
                {
                    residentName = $"{targetResident.FirstName} {targetResident.LastName}";
                    messageStatus += $" Misafir araç sahibi {residentName} kullanıcısına bildirim gönderildi.";

                    await _hubContext.Clients.User(targetResident.Id.ToString()).SendAsync("ReceiveNotification", new
                    {
                        Title = "🚗 Misafiriniz Siteye Giriş Yaptı",
                        Message = $"Tanımladığınız {cleanPlate} plakalı misafir aracınız otoparka giriş yapmıştır.",
                        Timestamp = DateTime.UtcNow,
                        Plate = cleanPlate
                    });

                    // Send native push notification for background/closed app delivery
                    try
                    {
                        await _pushService.SendNotificationToUserAsync(
                            targetResident.Id, 
                            "🚗 Misafiriniz Siteye Giriş Yaptı", 
                            $"Tanımladığınız {cleanPlate} plakalı misafir aracınız otoparka giriş yapmıştır."
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WebPush] Failed to trigger background push: {ex.Message}");
                    }
                }
                else
                {
                    messageStatus += " Araç misafir olarak kayıtlı ancak eşleşen site sakini bulunamadığı için bildirim gönderilemedi.";
                }

                // Deactivate the guest vehicle in both databases so it's removed from "Expected Guests"
                var vehicleToDeactivate = await _sitePassContext.Vehicles
                    .FirstOrDefaultAsync(v => v.Plate == cleanPlate && v.IsActive);
                    
                if (vehicleToDeactivate != null)
                {
                    vehicleToDeactivate.IsActive = false;
                    _sitePassContext.Entry(vehicleToDeactivate).State = EntityState.Modified;
                    await _sitePassContext.SaveChangesAsync();

                    // Deactivate in Excel
                    await _excelManager.DeactivateVehicleAsync(cleanPlate);
                }
            }

            return Ok(new
            {
                BariyerAcildi = true,
                Success = true,
                Message = messageStatus,
                Plate = cleanPlate,
                Owner = residentName,
                IsGuest = beyazListeKaydi.IsGuest,
                Timestamp = log.GirisTarihi
            });
        }

        private string NormalizePlate(string plate)
        {
            if (string.IsNullOrWhiteSpace(plate)) return string.Empty;
            var result = plate.Replace(" ", "");
            return result.ToUpper(new CultureInfo("tr-TR"));
        }
    }
}
