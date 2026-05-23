using Shared.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace Shared.Core.Entities;

/// <summary>
/// Records a discount applied to a sale, providing a full audit trail of all discount activity.
/// </summary>
public class SaleDiscount : ISoftDeletable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // --- Foreign keys ---

    public Guid SaleId { get; set; }

    /// <summary>
    /// Optional reference to the Discount master record (null for ad-hoc discounts)
    /// </summary>
    public Guid? DiscountId { get; set; }

    // --- Discount details ---

    /// <summary>
    /// Category of discount applied (Percentage, FixedAmount, Membership, Promotional)
    /// </summary>
    public SaleDiscountType DiscountType { get; set; }

    [Required]
    [MaxLength(100)]
    public string DiscountName { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable reason for the discount (e.g., "Gold member 10% off")
    /// </summary>
    [MaxLength(500)]
    public string? DiscountReason { get; set; }

    /// <summary>
    /// User ID of the manager/supervisor who authorized this discount (if required)
    /// </summary>
    public Guid? AuthorizedBy { get; set; }

    // --- Discount value ---

    /// <summary>
    /// Percentage value for percentage-based discounts (e.g., 10.00 = 10%)
    /// </summary>
    [Range(0, 100)]
    public decimal? PercentageValue { get; set; }

    /// <summary>
    /// Fixed monetary amount for fixed-amount discounts
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal? FixedAmount { get; set; }

    /// <summary>
    /// Actual monetary discount amount applied to the sale after calculation
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal CalculatedAmount { get; set; }

    /// <summary>
    /// Alias for CalculatedAmount — preserved for backward compatibility with existing services
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal DiscountAmount
    {
        get => CalculatedAmount;
        set => CalculatedAmount = value;
    }

    // --- Timestamps ---

    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    // --- Standard audit fields ---

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // --- Sync fields ---

    public SyncStatus SyncStatus { get; set; } = SyncStatus.NotSynced;
    public Guid DeviceId { get; set; }
    public DateTime? ServerSyncedAt { get; set; }

    // --- Soft delete ---

    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    // --- Navigation properties ---

    public virtual Sale Sale { get; set; } = null!;
    public virtual Discount? Discount { get; set; }
    public virtual ICollection<SaleItemDiscount> SaleItemDiscounts { get; set; } = new List<SaleItemDiscount>();
}
