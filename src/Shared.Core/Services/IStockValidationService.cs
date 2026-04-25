using Shared.Core.Entities;

namespace Shared.Core.Services;

/// <summary>
/// Service for validating stock availability and managing stock reservations during active sales.
/// Requirement 7.1: Check current inventory levels before allowing product addition to sales.
/// Requirement 7.2: Prevent addition when insufficient stock exists.
/// Requirement 7.3: Account for items already in active sales when calculating availability.
/// Requirement 7.4: Handle batch-tracked products with expiry date validation.
/// </summary>
public interface IStockValidationService
{
    /// <summary>
    /// Validates whether a product is available in the requested quantity for a given shop.
    /// Accounts for stock already reserved by active sales.
    /// Requirement 7.1, 7.3: Check current inventory considering active sale reservations.
    /// </summary>
    Task<StockAvailabilityResult> ValidateProductAvailabilityAsync(Guid productId, int requestedQuantity, Guid? shopId = null);

    /// <summary>
    /// Validates availability of a specific product batch, including expiry date check.
    /// Requirement 7.4: Handle batch-tracked products with expiry date validation.
    /// </summary>
    Task<StockAvailabilityResult> ValidateBatchAvailabilityAsync(Guid productId, string batchNumber, int requestedQuantity);

    /// <summary>
    /// Returns stock alerts for a collection of sale items (e.g., low stock, near expiry).
    /// </summary>
    Task<IEnumerable<StockAlert>> GetStockAlertsAsync(IEnumerable<SaleItem> saleItems);

    /// <summary>
    /// Reserves stock for a sale, reducing the available quantity for other concurrent sales.
    /// Requirement 7.3: Account for items already in active sales.
    /// </summary>
    Task<bool> ReserveStockAsync(Guid productId, int quantity, Guid saleId);

    /// <summary>
    /// Releases all stock reservations associated with a sale (e.g., on cancellation or completion).
    /// Requirement 2.6: Proper cleanup when items are removed or sale is cancelled.
    /// </summary>
    Task<bool> ReleaseStockReservationAsync(Guid saleId);

    /// <summary>
    /// Gets the current physical stock level for a product, optionally filtered by shop.
    /// </summary>
    Task<StockLevel> GetCurrentStockLevelAsync(Guid productId, Guid? shopId = null);

    /// <summary>
    /// Validates that a product is active and not expired before allowing it to be added to a sale.
    /// Requirement 2.1: Validate product existence and availability.
    /// Requirement 2.4: Prevent adding expired or inactive products.
    /// </summary>
    Task<ProductValidationResult> ValidateProductForSaleAsync(Guid productId);
}

/// <summary>
/// Result of a stock availability validation check.
/// </summary>
public class StockAvailabilityResult
{
    public bool IsAvailable { get; set; }
    public int AvailableQuantity { get; set; }
    public int RequestedQuantity { get; set; }
    public string? UnavailabilityReason { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public IEnumerable<StockAlert> Alerts { get; set; } = new List<StockAlert>();
}

/// <summary>
/// Represents the current stock level for a product.
/// </summary>
public class StockLevel
{
    public Guid ProductId { get; set; }
    public int PhysicalQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int AvailableQuantity => PhysicalQuantity - ReservedQuantity;
    public Guid? ShopId { get; set; }
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// A stock alert for a sale item (e.g., low stock, near expiry, out of stock).
/// </summary>
public class StockAlert
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public StockAlertType AlertType { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? CurrentQuantity { get; set; }
    public DateTime? ExpiryDate { get; set; }
}

/// <summary>
/// Types of stock alerts.
/// </summary>
public enum StockAlertType
{
    LowStock,
    OutOfStock,
    NearExpiry,
    Expired,
    InsufficientForRequest
}

/// <summary>
/// Result of validating a product's eligibility for sale.
/// </summary>
public class ProductValidationResult
{
    public bool IsValid { get; set; }
    public string? InvalidReason { get; set; }
    public bool ProductExists { get; set; }
    public bool IsActive { get; set; }
    public bool IsExpired { get; set; }
    public DateTime? ExpiryDate { get; set; }
}
