using System;

namespace SitePass.Core.Entities
{
    public class Vehicle
    {
        public int Id { get; set; }
        
        // Plate (plaka) - should be stored uppercase and space-free.
        public string Plate { get; set; } = string.Empty;
        
        public int ResidentId { get; set; }
        public virtual User Resident { get; set; } = null!;
        
        public bool IsGuest { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime? ExpireDate { get; set; } // Set to 12 hours from now for guests
        public bool IsActive { get; set; } = true;
    }
}
