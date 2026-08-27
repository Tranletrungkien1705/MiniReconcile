namespace MiniReconcile.Models;

public interface IOrgOwned { Guid OrgId { get; set; } }

public enum LedgerType { Debit = 0, Credit = 1 }          // Debit = HQ ghi nợ đại lý (hóa đơn); Credit = đại lý thanh toán
public enum StatementStatus { Draft = 0, Sent = 1, Confirmed = 2, Disputed = 3, Closed = 4 }

public class Org
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Partner : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public decimal OpeningBalance { get; set; }           // Dư nợ đầu kỳ mang sang (dương = đại lý nợ)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<LedgerEntry> Entries { get; set; } = new();
}

public class LedgerEntry : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int PartnerId { get; set; }
    public Partner? Partner { get; set; }
    public DateTime EntryDate { get; set; } = DateTime.Today;
    public string DocNo { get; set; } = "";               // Số chứng từ (hóa đơn / phiếu thu)
    public LedgerType Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime? DueDate { get; set; }                // Hạn thanh toán (với ghi nợ)
    public string? Note { get; set; }
    public int? StatementId { get; set; }                 // Đã đưa vào bảng đối soát nào (đóng băng)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public decimal Signed => Type == LedgerType.Debit ? Amount : -Amount;
}

public class Statement : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string No { get; set; } = "";
    public int PartnerId { get; set; }
    public Partner? Partner { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public decimal OpeningBalance { get; set; }           // Dư nợ đầu kỳ (đóng băng)
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal ClosingBalance { get; set; }           // = Opening + Debit - Credit
    public StatementStatus Status { get; set; } = StatementStatus.Draft;
    public DateTime? ConfirmedAt { get; set; }
    public string? DisputeNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
