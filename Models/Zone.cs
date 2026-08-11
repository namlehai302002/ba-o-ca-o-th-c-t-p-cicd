using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WMS.Models;

[Table("Zones")]
public class Zone
{
    [Key]
    public int ZoneId { get; set; }

    public int WarehouseId { get; set; }

    [Required, MaxLength(20)]
    public string ZoneCode { get; set; } = "";

    [Required, MaxLength(100)]
    public string ZoneName { get; set; } = "";

    public ZoneTypeEnum ZoneType { get; set; } = ZoneTypeEnum.Storage;

    public bool IsActive { get; set; } = true;

    [ForeignKey("WarehouseId")]
    public Warehouse Warehouse { get; set; } = null!;

    public ICollection<Location> Locations { get; set; } = new List<Location>();

    public string ZoneTypeName => ZoneType switch
    {
        ZoneTypeEnum.Storage => "Lưu trữ",
        ZoneTypeEnum.Receiving => "Tiếp nhận",
        ZoneTypeEnum.Shipping => "Xuất hàng",
        ZoneTypeEnum.Staging => "Cách ly/QC",
        ZoneTypeEnum.CrossDock => "Cross-dock",
        _ => "Khác"
    };
}
