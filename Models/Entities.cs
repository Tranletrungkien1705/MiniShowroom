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
/// <summary>Hình thức thanh toán (theo hồ sơ bán xe TCMotor: tiền mặt / vay trả góp qua ngân hàng).</summary>
public enum PayMethod { Cash = 0, BankTransfer = 1, Installment = 2 }

/// <summary>Mẫu xe trưng bày. Cột kỹ thuật lấy theo danh mục xe DMS.Sales (đời xe, nhiên liệu, số chỗ, phân khúc, bảo hành).</summary>
public class VehicleModel : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Variant { get; set; }              // phiên bản (bản Đặc biệt / Cao cấp...)
    public decimal ListPrice { get; set; }            // giá niêm yết (chưa gồm phí lăn bánh)
    public string? Color { get; set; }
    public int? ModelYear { get; set; }               // đời xe
    public string? FuelType { get; set; }             // Xăng / Dầu / Điện / Hybrid
    public int? Seats { get; set; }                   // số chỗ ngồi
    public string? Segment { get; set; }              // phân khúc (A/B/C/SUV/MPV...)
    public int WarrantyMonths { get; set; } = 36;     // thời hạn bảo hành (tháng)
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
    public string? IdentityNo { get; set; }           // CCCD/CMND (định danh người mua)
    public string? Address { get; set; }              // địa chỉ (đăng ký xe theo tỉnh)
    public LeadSource Source { get; set; }
    public int? ModelId { get; set; }                 // xe quan tâm
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

/// <summary>
/// Thương vụ / hồ sơ bán xe (báo giá → đặt cọc → giao xe).
/// Cột tài chính & định danh xe lấy theo hồ sơ bán xe thật (DMS.Sales/TCMotor):
/// VIN/số máy/số khung, giá lăn bánh (VAT + lệ phí trước bạ + phí biển số + bảo hiểm + phụ kiện), hình thức thanh toán.
/// </summary>
public class Deal : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";
    public int LeadId { get; set; }
    public int ModelId { get; set; }

    // ── Giá & chiết khấu ──
    public decimal Price { get; set; }                // giá xe theo báo giá (thường = ListPrice)
    public decimal Discount { get; set; }             // chiết khấu
    public decimal DepositAmount { get; set; }        // tiền đặt cọc

    // ── Phí lăn bánh (theo hồ sơ đăng ký xe) ──
    public decimal VatAmount { get; set; }            // thuế GTGT (VAT 10%)
    public decimal RegistrationFee { get; set; }      // lệ phí trước bạ
    public decimal PlateFee { get; set; }             // phí cấp biển số
    public decimal InsuranceAmount { get; set; }      // bảo hiểm (TNDS + vật chất)
    public decimal AccessoriesAmount { get; set; }    // phụ kiện kèm theo

    // ── Thanh toán ──
    public PayMethod PaymentMethod { get; set; } = PayMethod.Cash;
    public string? BankName { get; set; }             // ngân hàng cho vay (nếu trả góp)
    public decimal LoanAmount { get; set; }           // số tiền vay

    // ── Định danh xe được giao ──
    public string? Vin { get; set; }                  // số VIN
    public string? EngineNo { get; set; }             // số máy
    public string? ChassisNo { get; set; }            // số khung
    public string? Color { get; set; }                // màu xe giao
    public string? LicensePlate { get; set; }         // biển số (sau đăng ký)

    // ── Người mua (chốt trên hợp đồng) ──
    public string? BuyerName { get; set; }
    public string? BuyerIdNo { get; set; }
    public string? BuyerPhone { get; set; }
    public string? BuyerAddress { get; set; }
    public string? SalesPerson { get; set; }

    public DealStatus Status { get; set; } = DealStatus.Quoted;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? DepositAt { get; set; }
    public DateTime? ExpectedDelivery { get; set; }   // ngày hẹn giao
    public DateTime? DeliveredAt { get; set; }
    public string? InsurancePolicyCode { get; set; }  // mã BH TNDS tự lập khi giao xe (tích hợp MiniInsurance)

    public Lead Lead { get; set; } = null!;
    public VehicleModel Model { get; set; } = null!;

    public decimal FinalPrice => Price - Discount;
    /// <summary>Tổng giá lăn bánh khách phải trả (giá sau CK + VAT + các phí).</summary>
    public decimal TotalPayable => FinalPrice + VatAmount + RegistrationFee + PlateFee + InsuranceAmount + AccessoriesAmount;
    /// <summary>Còn phải thu = tổng lăn bánh − cọc − (vay nếu trả góp).</summary>
    public decimal Remaining => TotalPayable - DepositAmount - (PaymentMethod == PayMethod.Installment ? LoanAmount : 0);
}
