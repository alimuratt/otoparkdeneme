using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SitePass.Core.Entities
{
    [Table("BeyazListe")]
    public class BeyazListe
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        public string Plaka { get; set; } = string.Empty;

        [StringLength(100)]
        public string SahipAdSoyad { get; set; } = string.Empty;

        [StringLength(50)]
        public string BlokDaire { get; set; } = string.Empty;

        public bool IsGuest { get; set; }

        public bool IsActive { get; set; }

        public DateTime? ExpireDate { get; set; }
    }
}
