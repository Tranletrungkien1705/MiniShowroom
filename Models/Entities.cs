namespace MiniShowroom.Models;

public class Org
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
public interface IOrgOwned { Guid OrgId { get; set; } }

public enum LeadSource { WalkIn = 0, Hotline = 1, Facebook = 2, Website = 3, Referral = 4 }
/// <summary>Giai đoạn phễu bán hàng showroom.</summary>
public enum LeadStage { New = 0, Contacted = 1, TestDriven = 2, Quoted = 3, Deposited = 4, Delivered = 5, Lost = 6 }
public enum TestDriveStatus { Scheduled = 0, Done = 1, NoShow = 2, Cancelled = 3 }
public enum DealStatus { Quoted = 0, Deposited = 1, Delivered = 2, Cancelled = 3 }

/// <summary>Mẫu xe trưng bày.</summary>
public class VehicleModel : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Variant { get; set; }
    public decimal ListPrice { get; set; }
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Khách hàng tiềm năng (lead) — trung tâm phễu bán hàng.</summary>
public class Lead : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public LeadSource Source { get; set; }
    public int? ModelId { get; set; }              // xe quan tâm
    public LeadStage Stage { get; set; } = LeadStage.New;
    public string? SalesPerson { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LostAt { get; set; }

    public VehicleModel? Model { get; set; }
    public List<TestDrive> TestDrives { get; set; } = [];
    public List<Deal> Deals { get; set; } = [];
}

public class TestDrive : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int LeadId { get; set; }
    public int ModelId { get; set; }
    public DateTime ScheduledAt { get; set; } = DateTime.Now.AddDays(1);
    public TestDriveStatus Status { get; set; } = TestDriveStatus.Scheduled;
    public string? Note { get; set; }

    public Lead Lead { get; set; } = null!;
    public VehicleModel Model { get; set; } = null!;
}

/// <summary>Thương vụ (báo giá → đặt cọc → giao xe).</summary>
public class Deal : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";
    public int LeadId { get; set; }
    public int ModelId { get; set; }
    public decimal Price { get; set; }
    public decimal Discount { get; set; }
    public decimal DepositAmount { get; set; }
    public DealStatus Status { get; set; } = DealStatus.Quoted;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? DepositAt { get; set; }
    public DateTime? DeliveredAt { get; set; }

    public Lead Lead { get; set; } = null!;
    public VehicleModel Model { get; set; } = null!;

    public decimal FinalPrice => Price - Discount;
    public decimal Remaining => FinalPrice - DepositAmount;
}
