using MiniReconcile.Models;
namespace MiniReconcile.Services;

public static class Ui
{
    public static string Money(decimal v) => v.ToString("N0") + "đ";

    public static (string text, string css) Stmt(StatementStatus s) => s switch
    {
        StatementStatus.Draft     => ("Nháp", "secondary"),
        StatementStatus.Sent      => ("Đã gửi", "info"),
        StatementStatus.Confirmed => ("Đã xác nhận", "success"),
        StatementStatus.Disputed  => ("Khiếu nại", "danger"),
        StatementStatus.Closed    => ("Đã chốt", "dark"),
        _ => (s.ToString(), "secondary")
    };

    public static string Ledger(LedgerType t) => t == LedgerType.Debit ? "Ghi nợ" : "Thanh toán";
    public static string LedgerCss(LedgerType t) => t == LedgerType.Debit ? "text-danger" : "text-success";
}
