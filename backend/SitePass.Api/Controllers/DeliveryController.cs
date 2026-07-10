using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SitePass.Api.Hubs;
using SitePass.Core.Entities;
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
    public class DeliveryController : ControllerBase
    {
        private readonly SitePassDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly PushNotificationService _pushService;

        public DeliveryController(
            SitePassDbContext context, 
            IHubContext<NotificationHub> hubContext,
            PushNotificationService pushService)
        {
            _context = context;
            _hubContext = hubContext;
            _pushService = pushService;
        }

        public class CreateDeliveryRequest
        {
            public string DeliveryType { get; set; } = "Kargo"; // Default to Kargo
        }

        [HttpPost("expected")]
        [Authorize(Roles = "Resident")]
        public async Task<IActionResult> CreateExpectedDelivery([FromBody] CreateDeliveryRequest request)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var residentId))
            {
                return Unauthorized(new { Message = "Kullanıcı kimliği geçersiz." });
            }

            var delivery = new Delivery
            {
                ResidentId = residentId,
                DeliveryType = string.IsNullOrWhiteSpace(request.DeliveryType) ? "Kargo" : request.DeliveryType,
                IsActive = true,
                CreatedDate = DateTime.UtcNow,
                ExpireDate = DateTime.UtcNow.AddDays(1), // Valid for 24 hours
                IsProcessed = false
            };

            _context.Deliveries.Add(delivery);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                Message = "Teslimat beklentisi başarıyla kaydedildi. 24 saat geçerlidir.",
                Delivery = new
                {
                    delivery.Id,
                    delivery.DeliveryType,
                    delivery.IsActive,
                    delivery.CreatedDate,
                    delivery.ExpireDate,
                    delivery.IsProcessed
                }
            });
        }

        // Endpoint for Security to list all active expected deliveries
        [HttpGet("active-deliveries")]
        [Authorize(Roles = "Security,Admin")]
        public async Task<IActionResult> GetActiveDeliveries()
        {
            var now = DateTime.UtcNow;
            
            var deliveries = await _context.Deliveries
                .Include(d => d.Resident)
                .Where(d => d.IsActive && !d.IsProcessed && d.ExpireDate > now)
                .OrderByDescending(d => d.CreatedDate)
                .Select(d => new
                {
                    d.Id,
                    d.DeliveryType,
                    d.CreatedDate,
                    d.ExpireDate,
                    ResidentName = $"{d.Resident.FirstName} {d.Resident.LastName}",
                    ResidentPhone = d.Resident.PhoneNumber,
                    BlockNo = d.Resident.BlockNo ?? "",
                    ApartmentNo = d.Resident.ApartmentNo ?? ""
                })
                .ToListAsync();

            return Ok(deliveries);
        }

        // Endpoint for Security to approve/process the delivery entrance
        [HttpPost("approve/{id}")]
        [Authorize(Roles = "Security,Admin")]
        public async Task<IActionResult> ApproveDelivery(int id)
        {
            var delivery = await _context.Deliveries
                .Include(d => d.Resident)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (delivery == null)
            {
                return NotFound(new { Message = "Teslimat kaydı bulunamadı." });
            }

            if (delivery.IsProcessed || !delivery.IsActive)
            {
                return BadRequest(new { Message = "Bu teslimat zaten işlenmiş veya süresi dolmuş." });
            }

            // Mark as processed & inactive
            delivery.IsProcessed = true;
            delivery.IsActive = false;

            await _context.SaveChangesAsync();

            // Trigger SignalR notification to the resident
            var residentIdString = delivery.ResidentId.ToString();
            string title = "📦 Teslimatınız Ulaştı";
            string message = $"Beklediğiniz '{delivery.DeliveryType}' teslimatınız siteye giriş yapmış / güvenliğe ulaşmıştır.";

            await _hubContext.Clients.User(residentIdString).SendAsync("ReceiveNotification", new
            {
                Title = title,
                Message = message,
                Timestamp = DateTime.UtcNow,
                DeliveryType = delivery.DeliveryType
            });

            // Send native push notification for background/closed app delivery
            try
            {
                await _pushService.SendNotificationToUserAsync(
                    delivery.ResidentId,
                    title,
                    message
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebPush] Failed to trigger background delivery push: {ex.Message}");
            }

            return Ok(new { Message = "Teslimat siteye giriş olarak onaylandı, sakine bildirim gönderildi." });
        }

        // Endpoint for Residents to list their own active expected deliveries
        [HttpGet("my-active")]
        [Authorize(Roles = "Resident")]
        public async Task<IActionResult> GetMyActiveDeliveries()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var residentId))
            {
                return Unauthorized(new { Message = "Kullanıcı kimliği geçersiz." });
            }

            var now = DateTime.UtcNow;

            var deliveries = await _context.Deliveries
                .Where(d => d.ResidentId == residentId && d.IsActive && !d.IsProcessed && d.ExpireDate > now)
                .OrderByDescending(d => d.CreatedDate)
                .Select(d => new
                {
                    d.Id,
                    d.DeliveryType,
                    d.CreatedDate,
                    d.ExpireDate
                })
                .ToListAsync();

            return Ok(deliveries);
        }

        // Endpoint for Residents to cancel their own expected delivery
        [HttpPost("cancel/{id}")]
        [Authorize(Roles = "Resident")]
        public async Task<IActionResult> CancelDelivery(int id)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var residentId))
            {
                return Unauthorized(new { Message = "Kullanıcı kimliği geçersiz." });
            }

            var delivery = await _context.Deliveries.FindAsync(id);

            if (delivery == null)
            {
                return NotFound(new { Message = "Teslimat kaydı bulunamadı." });
            }

            if (delivery.ResidentId != residentId)
            {
                return Forbid();
            }

            if (!delivery.IsActive || delivery.IsProcessed)
            {
                return BadRequest(new { Message = "Bu teslimat zaten aktif değil veya işlenmiş." });
            }

            delivery.IsActive = false;
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Teslimat beklentisi başarıyla iptal edildi." });
        }
    }
}
