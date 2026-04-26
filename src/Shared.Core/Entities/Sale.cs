using Shared.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace Shared.Core.Entities;

public class Sale : ISoftDeletable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// The shop where this sale was processed
    /// </summary>
    [Required]
    public Guid ShopId { get; set; }
    
    /// <summary>
    /// The user who processed this sale
    /// </summary>
    [Required]
    public Guid UserId { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string InvoiceNumber { get; set; } = string.Empty;
    
    // --- Legacy totals (preserved for backward compatibility) ---
    
    [Range(0, double.MaxValue)]
    public decimal TotalAmount { get; set; }
    
    [Range(0, double.MaxValue)]
    public decimal DiscountAmount { get; set; } = 0;
    
    [Range(0, double.MaxValue)]
    public decimal TaxAmount { get; set; } = 0;
    
    [Range(0, double.MaxValue)]
    public decimal MembershipDiscountAmount { get; set; } = 0;
    
    // --- Enhanced calculated totals ---
    
    /// <summary>
    /// Sum of all line subtotals before discounts and taxes
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal Subtotal { get; set; } = 0;
    
    /// <summary>
    /// Total discount amount applied across all items and sale-level discounts
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal TotalDiscount { get; set; } = 0;
    
    /// <summary>
    /// Total tax amount calculated across all items
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal TotalTax { get; set; } = 0;
    
    /// <summary>
    /// Final amount due after all discounts and taxes: Subtotal - TotalDiscount + TotalTax
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal FinalTotal { get; set; } = 0;
    
    // --- Payment information ---
    
    public PaymentMethod PaymentMethod { get; set; }
    
    /// <summary>
    /// Amount actually paid by the customer
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal AmountPaid { get; set; } = 0;
    
    /// <summary>
    /// Change returned to the customer (AmountPaid - FinalTotal)
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal ChangeAmount { get; set; } = 0;
    
    // --- Status and timestamps ---
    
    /// <summary>
    /// Current lifecycle status of the sale transaction
    /// </summary>
    public SaleStatus Status { get; set; } = SaleStatus.Draft;
    
    /// <summary>
    /// Timestamp when the sale was completed (payment received)
    /// </summary>
    public DateTime? CompletedAt { get; set; }
    
    /// <summary>
    /// Timestamp when the sale was cancelled
    /// </summary>
    public DateTime? CancelledAt { get; set; }
    
    /// <summary>
    /// Reason provided when the sale was cancelled
    /// </summary>
    [MaxLength(500)]
    public string? CancellationReason { get; set; }
    
    // --- Customer and membership ---
    
    public Guid? CustomerId { get; set; }
    public virtual Customer? Customer { get; set; }
    
    // --- Standard audit fields ---
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Tracks the last time any field on this sale was modified (for sync conflict resolution)
    /// </summary>
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    
    public Guid DeviceId { get; set; }
    
    public DateTime? ServerSyncedAt { get; set; }
    
    public SyncStatus SyncStatus { get; set; } = SyncStatus.NotSynced;
    
    // --- Soft delete properties ---
    
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    
    // --- Navigation properties ---
    
    public virtual Shop Shop { get; set; } = null!;
    public virtual User User { get; set; } = null!;
    public virtual ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
    public virtual ICollection<SaleDiscount> AppliedDiscounts { get; set; } = new List<SaleDiscount>();
    public virtual ICollection<SalePayment> Payments { get; set; } = new List<SalePayment>();
}