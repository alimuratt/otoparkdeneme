using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SitePass.Core.Entities
{
    [Table("GirisCikisLoglari")]
    public class GirisCikisLoglari
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int LogId { get; set; }

        [Required]
        [StringLength(20)]
        public string OkunanPlaka { get; set; } = string.Empty;

        public DateTime GirisTarihi { get; set; }

        [StringLength(50)]
        public string KameraKodu { get; set; } = string.Empty;
    }
}
