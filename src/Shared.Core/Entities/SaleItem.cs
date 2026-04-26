using System.ComponentModel.DataAnnotations;

namespace Shared.Core.Entities;

public class SaleItem : ISoftDeletable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid SaleId { get; set; }
    
    public Guid ProductId { get; set; }
    
    // --- Quantity and weight ---
    
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
    
    /// <summary>
    /// Weight in kilograms for weight-based products
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal? Weight { get; set; }
    
    [Range(0, double.MaxValue)]
    public decimal? RatePerKilogram { get; set; }
    
    /// <summary>
    /// Indicates whether this item is priced by weight rather than discrete quantity.
    /// When true, LineSubtotal = Weight * RatePerKilogram.
    /// </summary>
    public bool IsWeightBased { get; set; } = false;
    
    // --- Pricing ---
    
    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; set; }
    
    /// <summary>
    /// Line subtotal before discounts and taxes (Quantity * UnitPrice, or Weight * RatePerKilogram)
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal LineSubtotal { get; set; } = 0;
    
    /// <summary>
    /// Total discount applied to this line item
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal LineDiscount { get; set; } = 0;
    
    /// <summary>
    /// Tax amount for this line item
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal LineTax { get; set; } = 0;
    
    /// <summary>
    /// Final line total: LineSubtotal - LineDiscount + LineTax
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal LineTotal { get; set; } = 0;
    
    /// <summary>
    /// Legacy total price field (preserved for backward compatibility)
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal TotalPrice { get; set; }
    
    // --- Denormalized product information (for performance and historical accuracy) ---
    
    /// <summary>
    /// Product name at time of sale (denormalized to preserve historical accuracy)
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string ProductName { get; set; } = string.Empty;
    
    /// <summary>
    /// Product code/SKU at time of sale
    /// </summary>
    [MaxLength(50)]
    public string? ProductCode { get; set; }
    
    /// <summary>
    /// Product barcode at time of sale
    /// </summary>
    [MaxLength(50)]
    public string? Barcode { get; set; }
    
    /// <summary>
    /// Batch number for batch-tracked products (e.g., medicine)
    /// </summary>
    [MaxLength(50)]
    public string? BatchNumber { get; set; }
    
    /// <summary>
    /// Expiry date for batch-tracked products
    /// </summary>
    public DateTime? ExpiryDate { get; set; }
    
    // --- Soft delete properties ---
    
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    
    // --- Navigation properties ---
    
    public virtual Sale Sale { get; set; } = null!;
    public virtual Product Product { get; set; } = null!;
    public virtual ICollection<SaleItemDiscount> AppliedDiscounts { get; set; } = new List<SaleItemDiscount>();
}