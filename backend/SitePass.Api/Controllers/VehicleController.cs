using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SitePass.Api.Hubs;
using SitePass.Core.Entities;
using SitePass.Core.Enums;
using SitePass.Infrastructure.Data;
using SitePass.Infrastructure.Services;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SitePass.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class VehicleController : ControllerBase
    {
        private readonly SitePassDbContext _context;
        private readonly ExcelManager _excelManager;
        private readonly IHubContext<NotificationHub> _hubContext;

        public VehicleController(SitePassDbContext context, ExcelManager excelManager, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _excelManager = excelManager;
            _hubContext = hubContext;
        }

        public class AddGuestVehicleRequest
        {
            public string Plate { get; set; } = string.Empty;
        }

        // Normalize plate: remove spaces, convert to uppercase with Turkish support
        private string NormalizePlate(string plate)
        {
            if (string.IsNullOrWhiteSpace(plate)) return string.Empty;
            
            // Remove all spaces
            var result = plate.Replace(" ", "");
            
            // Turkish culture-aware uppercase
            return result.ToUpper(new System.Globalization.CultureInfo("tr-TR"));
        }

        [HttpPost("guest")]
        [Authorize(Roles = "Resident")]
        public async Task<IActionResult> AddGuestVehicle([FromBody] AddGuestVehicleRequest request)
        {
            var cleanPlate = NormalizePlate(request.Plate);
            if (string.IsNullOrEmpty(cleanPlate))
            {
                return BadRequest(new { Message = "Plaka boş olamaz." });
            }

            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var residentId))
            {
                return Unauthorized(new { Message = "Kullanıcı kimliği geçersiz." });
            }

            // Check if the plate is already registered (active or inactive)
            var existingVehicle = await _context.Vehicles
                .FirstOrDefaultAsync(v => v.Plate == cleanPlate);

            Vehicle guestVehicle;

            if (existingVehicle != null)
            {
                if (existingVehicle.IsActive)
                {
                    if (existingVehicle.IsGuest)
                    {
                        return BadRequest(new { Message = $"Bu plaka zaten aktif bir misafir olarak kayıtlı. Kalan Süre: {existingVehicle.ExpireDate}" });
                    }
                    else
                    {
                        return BadRequest(new { Message = "Bu plaka zaten kalıcı/sabit araç olarak kayıtlı." });
                    }
                }

                // Reactivate existing inactive vehicle record
                existingVehicle.IsActive = true;
                existingVehicle.IsGuest = true;
                existingVehicle.ResidentId = residentId;
                existingVehicle.CreatedDate = DateTime.UtcNow;
                existingVehicle.ExpireDate = DateTime.UtcNow.AddHours(12);

                _context.Entry(existingVehicle).State = EntityState.Modified;
                await _context.SaveChangesAsync();

                guestVehicle = existingVehicle;
            }
            else
            {
                // Create new vehicle record
                guestVehicle = new Vehicle
                {
                    Plate = cleanPlate,
                    ResidentId = residentId,
                    IsGuest = true,
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow,
                    ExpireDate = DateTime.UtcNow.AddHours(12) // 12 hours validity
                };

                _context.Vehicles.Add(guestVehicle);
                await _context.SaveChangesAsync();
            }

            // Sitenin harici otopark Excel dosyasına senkronize et
            try
            {
                var resident = await _context.Users.FindAsync(residentId);
                var otoparkVehicle = new BeyazListe
                {
                    Plaka = cleanPlate,
                    SahipAdSoyad = resident != null ? $"{resident.FirstName} {resident.LastName}" : "Bilinmeyen Sakin",
                    BlokDaire = resident != null ? $"{resident.BlockNo}-{resident.ApartmentNo}" : "",
                    IsGuest = true,
                    IsActive = true,
                    ExpireDate = DateTime.Now.AddHours(12) // Varsayılan 12 saat
                };

                // TEST NOTU: Evde hızlı süre takip simülasyonu yapabilmek için süreyi saniye cinsinden ayarlayabilirsiniz.
                // Testleri 30 saniyede patlatmak için aşağıdaki 2 satırın yorumunu açabilirsiniz:
                // otoparkVehicle.ExpireDate = DateTime.Now.AddSeconds(30);
                // guestVehicle.ExpireDate = DateTime.UtcNow.AddSeconds(30);
                // _context.Entry(guestVehicle).State = EntityState.Modified;
                // await _context.SaveChangesAsync();

                await _excelManager.AddGuestVehicleAsync(otoparkVehicle);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Excel Beyaz Liste senkronizasyon hatası: {ex.Message}");
            }

            return Ok(new
            {
                Message = "Misafir araç başarıyla tanımlandı. 12 saat geçerlidir.",
                Vehicle = new
                {
                    guestVehicle.Id,
                    guestVehicle.Plate,
                    guestVehicle.IsGuest,
                    guestVehicle.CreatedDate,
                    guestVehicle.ExpireDate,
                    guestVehicle.IsActive
                }
            });
        }

        [HttpGet("active-guests")]
        [Authorize(Roles = "Resident")]
        public async Task<IActionResult> GetActiveGuests()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var residentId))
            {
                return Unauthorized(new { Message = "Kullanıcı kimliği geçersiz." });
            }

            var now = DateTime.UtcNow;
            var activeGuests = await _context.Vehicles
                .Where(v => v.ResidentId == residentId && v.IsActive && v.IsGuest && (v.ExpireDate == null || v.ExpireDate > now))
                .Select(v => new
                {
                    v.Id,
                    v.Plate,
                    v.CreatedDate,
                    v.ExpireDate,
                    RemainingSeconds = v.ExpireDate.HasValue ? (int)Math.Max(0, (v.ExpireDate.Value - now).TotalSeconds) : 0
                })
                .ToListAsync();

            return Ok(activeGuests);
        }

        // Mock LPR (License Plate Recognition) trigger endpoint
        [HttpPost("mock-lpr")]
        [AllowAnonymous] // Allow simulation without JWT for convenience
        public async Task<IActionResult> MockLpr([FromBody] AddGuestVehicleRequest request)
        {
            var cleanPlate = NormalizePlate(request.Plate);
            if (string.IsNullOrEmpty(cleanPlate))
            {
                return BadRequest(new { Message = "Plaka boş olamaz." });
            }

            var now = DateTime.UtcNow;
            
            // Check if the plate is active (either guest with valid time, or permanent)
            var vehicle = await _context.Vehicles
                .Include(v => v.Resident)
                .FirstOrDefaultAsync(v => v.Plate == cleanPlate && v.IsActive && 
                                         (!v.IsGuest || v.ExpireDate == null || v.ExpireDate > now));

            if (vehicle == null)
            {
                return BadRequest(new { Success = false, Message = $"Geçersiz veya süresi dolmuş plaka: {cleanPlate}. Bariyer açılmadı!" });
            }

            // Plate is valid! Trigger SignalR notification
            var residentIdString = vehicle.ResidentId.ToString();
            var residentName = $"{vehicle.Resident.FirstName} {vehicle.Resident.LastName}";
            
            string title = "🚗 Araç Girişi Gerçekleşti";
            string message = vehicle.IsGuest 
                ? $"Tanımladığınız {cleanPlate} plakalı misafir aracınız siteye giriş yapmıştır."
                : $"{cleanPlate} plakalı kalıcı aracınız siteye giriş yapmıştır.";

            // Send via SignalR
            await _hubContext.Clients.User(residentIdString).SendAsync("ReceiveNotification", new
            {
                Title = title,
                Message = message,
                Timestamp = DateTime.UtcNow,
                Plate = cleanPlate
            });

            return Ok(new
            {
                Success = true,
                Message = $"Bariyer Açıldı! Sakine ({residentName}) bildirim gönderildi.",
                Plate = cleanPlate,
                Resident = residentName,
                IsGuest = vehicle.IsGuest
            });
        }

        [HttpPost("cancel/{id}")]
        [Authorize(Roles = "Resident")]
        public async Task<IActionResult> CancelGuestVehicle(int id)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var residentId))
            {
                return Unauthorized(new { Message = "Kullanıcı kimliği geçersiz." });
            }

            var vehicle = await _context.Vehicles.FindAsync(id);

            if (vehicle == null)
            {
                return NotFound(new { Message = "Araç kaydı bulunamadı." });
            }

            if (vehicle.ResidentId != residentId)
            {
                return Forbid();
            }

            if (!vehicle.IsGuest)
            {
                return BadRequest(new { Message = "Sadece misafir araçlar iptal edilebilir." });
            }

            if (!vehicle.IsActive)
            {
                return BadRequest(new { Message = "Bu araç zaten aktif değil." });
            }

            vehicle.IsActive = false;
            await _context.SaveChangesAsync();

            // Sitenin harici otopark Excel kaydını da iptal et (IsActive = false yap)
            try
            {
                await _excelManager.DeactivateVehicleAsync(vehicle.Plate);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Excel Beyaz Liste iptal senkronizasyon hatası: {ex.Message}");
            }

            return Ok(new { Message = "Misafir araç izni başarıyla iptal edildi." });
        }
    }
}
