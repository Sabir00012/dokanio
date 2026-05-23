using System.ComponentModel.DataAnnotations;

namespace Shared.Core.Entities;

/// <summary>
/// Junction entity that tracks how much of a sale-level discount was allocated
/// to a specific sale item, enabling per-item discount breakdowns.
/// </summary>
public class SaleItemDiscount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // --- Foreign keys ---

    public Guid SaleItemId { get; set; }

    public Guid SaleDiscountId { get; set; }

    // --- Discount allocation ---

    /// <summary>
    /// Monetary discount amount allocated to this specific line item from the parent SaleDiscount
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal DiscountAmount { get; set; }

    // --- Timestamps ---

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // --- Navigation properties ---

    public virtual SaleItem SaleItem { get; set; } = null!;
    public virtual SaleDiscount SaleDiscount { get; set; } = null!;
}
