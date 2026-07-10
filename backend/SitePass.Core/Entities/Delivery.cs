using System;

namespace SitePass.Core.Entities
{
    public class Delivery
    {
        public int Id { get; set; }
        
        public int ResidentId { get; set; }
        public virtual User Resident { get; set; } = null!;
        
        // DeliveryType (e.g. Kargo, Yemek, Sipariş)
        public string DeliveryType { get; set; } = string.Empty;
        
        public bool IsActive { get; set; } = true;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime ExpireDate { get; set; } = DateTime.UtcNow.AddDays(1); // 1 day after
        
        // Whether security processed it
        public bool IsProcessed { get; set; } = false;
    }
}
