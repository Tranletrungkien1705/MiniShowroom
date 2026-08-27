using MiniShowroom.Models;

namespace MiniShowroom.Services;

public static class Ui
{
    public static (string text, string css) Stage(LeadStage s) => s switch
    {
        LeadStage.New => ("Mới", "secondary"),
        LeadStage.Contacted => ("Đã liên hệ", "info"),
        LeadStage.TestDriven => ("Đã lái thử", "primary"),
        LeadStage.Quoted => ("Đã báo giá", "primary"),
        LeadStage.Deposited => ("Đã đặt cọc", "warning"),
        LeadStage.Delivered => ("Đã giao xe", "success"),
        LeadStage.Lost => ("Mất khách", "danger"),
        _ => (s.ToString(), "secondary")
    };
    public static string Source(LeadSource s) => s switch
    {
        LeadSource.WalkIn => "Đến showroom", LeadSource.Hotline => "Hotline", LeadSource.Facebook => "Facebook",
        LeadSource.Website => "Website", LeadSource.Referral => "Giới thiệu", _ => s.ToString()
    };
    public static (string text, string css) Deal(DealStatus s) => s switch
    {
        DealStatus.Quoted => ("Báo giá", "info"),
        DealStatus.Deposited => ("Đã cọc", "warning"),
        DealStatus.Delivered => ("Đã giao", "success"),
        DealStatus.Cancelled => ("Đã hủy", "dark"),
        _ => (s.ToString(), "secondary")
    };
    public static (string text, string css) TD(TestDriveStatus s) => s switch
    {
        TestDriveStatus.Scheduled => ("Đã hẹn", "info"),
        TestDriveStatus.Done => ("Hoàn thành", "success"),
        TestDriveStatus.NoShow => ("Không đến", "warning"),
        TestDriveStatus.Cancelled => ("Đã hủy", "dark"),
        _ => (s.ToString(), "secondary")
    };
}
