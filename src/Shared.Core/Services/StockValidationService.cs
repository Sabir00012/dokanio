using Microsoft.Extensions.Logging;
using Shared.Core.Entities;
using Shared.Core.Enums;
using Shared.Core.Repositories;

namespace Shared.Core.Services;

/// <summary>
/// Production-ready stock validation service that checks real inventory levels,
/// manages stock reservations for active sales, and validates product eligibility.
///
/// Requirement 7.1: Check current inventory levels before allowing product addition.
/// Requirement 7.2: Prevent addition when insufficient stock exists.
/// Requirement 7.3: Account for items already in active sales when calculating availability.
/// Requirement 7.4: Handle batch-tracked products with expiry date validation.
/// Requirement 2.1: Validate product existence and availability.
/// Requirement 2.4: Prevent adding expired or inactive products.
/// Requirement 2.6: Proper cleanup when items are removed or sale is cancelled.
/// </summary>
public class StockValidationService : IStockValidationService
{
    private readonly IStockRepository _stockRepository;
    private readonly IProductRepository _productRepository;
    private readonly ISaleItemRepository _saleItemRepository;
    private readonly ISaleRepository _saleRepository;
    private readonly ILogger<StockValidationService> _logger;

    // In-memory reservation store: maps saleId -> list of (productId, reservedQty)
    // In a production multi-process environment this would be backed by a distributed cache or DB table.
    private static readonly Dictionary<Guid, List<StockReservation>> _reservations = new();
    private static readonly object _reservationLock = new();

    public StockValidationService(
        IStockRepository stockRepository,
        IProductRepository productRepository,
        ISaleItemRepository saleItemRepository,
        ISaleRepository saleRepository,
        ILogger<StockValidationService> logger)
    {
        _stockRepository = stockRepository;
        _productRepository = productRepository;
        _saleItemRepository = saleItemRepository;
        _saleRepository = saleRepository;
        _logger = logger;
    }

    // =========================================================================
    // Product Validation
    // =========================================================================

    /// <summary>
    /// Validates that a product exists, is active, and is not expired.
    /// Requirement 2.1: Validate product existence and availability.
    /// Requirement 2.4: Prevent adding expired or inactive products.
    /// </summary>
    public async Task<ProductValidationResult> ValidateProductForSaleAsync(Guid productId)
    {
        if (productId == Guid.Empty)
        {
            return new ProductValidationResult
            {
                IsValid = false,
                ProductExists = false,
                IsActive = false,
                IsExpired = false,
                InvalidReason = "Product ID cannot be empty."
            };
        }

        var product = await _productRepository.GetByIdAsync(productId);

        if (product == null || product.IsDeleted)
        {
            _logger.LogWarning("Product {ProductId} not found or deleted", productId);
            return new ProductValidationResult
            {
                IsValid = false,
                ProductExists = false,
                IsActive = false,
                IsExpired = false,
                InvalidReason = $"Product {productId} does not exist."
            };
        }

        if (!product.IsActive)
        {
            _logger.LogWarning("Product {ProductId} ({Name}) is inactive and cannot be added to a sale", productId, product.Name);
            return new ProductValidationResult
            {
                IsValid = false,
                ProductExists = true,
                IsActive = false,
                IsExpired = false,
                InvalidReason = $"Product '{product.Name}' is inactive and cannot be sold."
            };
        }

        if (product.ExpiryDate.HasValue && product.ExpiryDate.Value < DateTime.UtcNow)
        {
            _logger.LogWarning("Product {ProductId} ({Name}) expired on {ExpiryDate} and cannot be added to a sale",
                productId, product.Name, product.ExpiryDate.Value);
            return new ProductValidationResult
            {
                IsValid = false,
                ProductExists = true,
                IsActive = true,
                IsExpired = true,
                ExpiryDate = product.ExpiryDate,
                InvalidReason = $"Product '{product.Name}' expired on {product.ExpiryDate.Value:yyyy-MM-dd} and cannot be sold."
            };
        }

        return new ProductValidationResult
        {
            IsValid = true,
            ProductExists = true,
            IsActive = true,
            IsExpired = false,
            ExpiryDate = product.ExpiryDate
        };
    }

    // =========================================================================
    // Stock Availability Validation
    // =========================================================================

    /// <summary>
    /// Validates whether a product is available in the requested quantity.
    /// Accounts for stock already reserved by other active sales.
    /// Requirement 7.1, 7.3: Check current inventory considering active sale reservations.
    /// </summary>
    public async Task<StockAvailabilityResult> ValidateProductAvailabilityAsync(
        Guid productId, int requestedQuantity, Guid? shopId = null)
    {
        if (productId == Guid.Empty)
        {
            return new StockAvailabilityResult
            {
                IsAvailable = false,
                RequestedQuantity = requestedQuantity,
                AvailableQuantity = 0,
                UnavailabilityReason = "Product ID cannot be empty."
            };
        }

        if (requestedQuantity <= 0)
        {
            return new StockAvailabilityResult
            {
                IsAvailable = false,
                RequestedQuantity = requestedQuantity,
                AvailableQuantity = 0,
                UnavailabilityReason = "Requested quantity must be greater than zero."
            };
        }

        try
        {
            var stockLevel = await GetCurrentStockLevelAsync(productId, shopId);
            var availableQty = stockLevel.AvailableQuantity;

            var alerts = new List<StockAlert>();

            if (availableQty <= 0)
            {
                alerts.Add(new StockAlert
                {
                    ProductId = productId,
                    AlertType = StockAlertType.OutOfStock,
                    Message = "Product is out of stock.",
                    CurrentQuantity = stockLevel.PhysicalQuantity
                });

                _logger.LogWarning("Product {ProductId} is out of stock (physical: {Physical}, reserved: {Reserved})",
                    productId, stockLevel.PhysicalQuantity, stockLevel.ReservedQuantity);

                return new StockAvailabilityResult
                {
                    IsAvailable = false,
                    RequestedQuantity = requestedQuantity,
                    AvailableQuantity = 0,
                    UnavailabilityReason = "Product is out of stock. Available: 0 units.",
                    Alerts = alerts
                };
            }

            if (availableQty < requestedQuantity)
            {
                alerts.Add(new StockAlert
                {
                    ProductId = productId,
                    AlertType = StockAlertType.InsufficientForRequest,
                    Message = $"Insufficient stock. Requested: {requestedQuantity}, Available: {availableQty}.",
                    CurrentQuantity = availableQty
                });

                _logger.LogWarning(
                    "Insufficient stock for product {ProductId}: requested {Requested}, available {Available}",
                    productId, requestedQuantity, availableQty);

                return new StockAvailabilityResult
                {
                    IsAvailable = false,
                    RequestedQuantity = requestedQuantity,
                    AvailableQuantity = availableQty,
                    UnavailabilityReason = $"Insufficient stock. Requested: {requestedQuantity}, Available: {availableQty} units.",
                    Alerts = alerts
                };
            }

            // Add low-stock warning if stock is running low (less than 5 units remaining after this sale)
            if (availableQty - requestedQuantity < 5)
            {
                alerts.Add(new StockAlert
                {
                    ProductId = productId,
                    AlertType = StockAlertType.LowStock,
                    Message = $"Low stock warning: only {availableQty - requestedQuantity} units will remain after this sale.",
                    CurrentQuantity = availableQty
                });
            }

            return new StockAvailabilityResult
            {
                IsAvailable = true,
                RequestedQuantity = requestedQuantity,
                AvailableQuantity = availableQty,
                Alerts = alerts
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating stock availability for product {ProductId}", productId);
            return new StockAvailabilityResult
            {
                IsAvailable = false,
                RequestedQuantity = requestedQuantity,
                AvailableQuantity = 0,
                UnavailabilityReason = "Unable to validate stock availability due to an internal error."
            };
        }
    }

    /// <summary>
    /// Validates availability of a specific product batch, including expiry date check.
    /// Requirement 7.4: Handle batch-tracked products with expiry date validation.
    /// </summary>
    public async Task<StockAvailabilityResult> ValidateBatchAvailabilityAsync(
        Guid productId, string batchNumber, int requestedQuantity)
    {
        if (productId == Guid.Empty || string.IsNullOrWhiteSpace(batchNumber))
        {
            return new StockAvailabilityResult
            {
                IsAvailable = false,
                RequestedQuantity = requestedQuantity,
                AvailableQuantity = 0,
                UnavailabilityReason = "Product ID and batch number are required."
            };
        }

        try
        {
            var product = await _productRepository.GetByIdAsync(productId);
            if (product == null || product.IsDeleted)
            {
                return new StockAvailabilityResult
                {
                    IsAvailable = false,
                    RequestedQuantity = requestedQuantity,
                    AvailableQuantity = 0,
                    UnavailabilityReason = "Product not found."
                };
            }

            // Validate batch expiry using product-level expiry date
            if (product.ExpiryDate.HasValue && product.ExpiryDate.Value < DateTime.UtcNow)
            {
                _logger.LogWarning("Batch {BatchNumber} of product {ProductId} is expired (expiry: {ExpiryDate})",
                    batchNumber, productId, product.ExpiryDate.Value);

                return new StockAvailabilityResult
                {
                    IsAvailable = false,
                    RequestedQuantity = requestedQuantity,
                    AvailableQuantity = 0,
                    ExpiryDate = product.ExpiryDate,
                    UnavailabilityReason = $"Batch '{batchNumber}' expired on {product.ExpiryDate.Value:yyyy-MM-dd}.",
                    Alerts = new List<StockAlert>
                    {
                        new StockAlert
                        {
                            ProductId = productId,
                            AlertType = StockAlertType.Expired,
                            Message = $"Batch '{batchNumber}' has expired.",
                            ExpiryDate = product.ExpiryDate
                        }
                    }
                };
            }

            // Check how many units of this batch are already in active sales
            var activeSaleItems = await _saleItemRepository.GetByProductBatchAsync(productId, batchNumber);
            var activeSales = await _saleRepository.FindAsync(s =>
                s.Status == SaleStatus.Active || s.Status == SaleStatus.Draft);
            var activeSaleIds = activeSales.Select(s => s.Id).ToHashSet();

            var reservedInActiveSales = activeSaleItems
                .Where(si => activeSaleIds.Contains(si.SaleId) && !si.IsDeleted)
                .Sum(si => si.Quantity);

            var physicalStock = await _stockRepository.GetStockQuantityAsync(productId);
            var availableQty = Math.Max(0, physicalStock - reservedInActiveSales);

            var alerts = new List<StockAlert>();

            // Near-expiry warning (within 7 days)
            if (product.ExpiryDate.HasValue && product.ExpiryDate.Value <= DateTime.UtcNow.AddDays(7))
            {
                alerts.Add(new StockAlert
                {
                    ProductId = productId,
                    AlertType = StockAlertType.NearExpiry,
                    Message = $"Batch '{batchNumber}' expires on {product.ExpiryDate.Value:yyyy-MM-dd}.",
                    ExpiryDate = product.ExpiryDate
                });
            }

            if (availableQty < requestedQuantity)
            {
                return new StockAvailabilityResult
                {
                    IsAvailable = false,
                    RequestedQuantity = requestedQuantity,
                    AvailableQuantity = availableQty,
                    ExpiryDate = product.ExpiryDate,
                    UnavailabilityReason = $"Insufficient stock for batch '{batchNumber}'. Available: {availableQty}.",
                    Alerts = alerts
                };
            }

            return new StockAvailabilityResult
            {
                IsAvailable = true,
                RequestedQuantity = requestedQuantity,
                AvailableQuantity = availableQty,
                ExpiryDate = product.ExpiryDate,
                Alerts = alerts
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating batch availability for product {ProductId}, batch {BatchNumber}",
                productId, batchNumber);
            return new StockAvailabilityResult
            {
                IsAvailable = false,
                RequestedQuantity = requestedQuantity,
                AvailableQuantity = 0,
                UnavailabilityReason = "Unable to validate batch availability due to an internal error."
            };
        }
    }

    // =========================================================================
    // Stock Alerts
    // =========================================================================

    /// <summary>
    /// Returns stock alerts for a collection of sale items.
    /// </summary>
    public async Task<IEnumerable<StockAlert>> GetStockAlertsAsync(IEnumerable<SaleItem> saleItems)
    {
        var alerts = new List<StockAlert>();

        foreach (var item in saleItems.Where(i => !i.IsDeleted))
        {
            try
            {
                var product = await _productRepository.GetByIdAsync(item.ProductId);
                if (product == null) continue;

                var stockLevel = await GetCurrentStockLevelAsync(item.ProductId);

                // Out of stock alert
                if (stockLevel.PhysicalQuantity <= 0)
                {
                    alerts.Add(new StockAlert
                    {
                        ProductId = item.ProductId,
                        ProductName = product.Name,
                        AlertType = StockAlertType.OutOfStock,
                        Message = $"'{product.Name}' is out of stock.",
                        CurrentQuantity = 0
                    });
                }
                // Low stock alert (less than 5 units)
                else if (stockLevel.AvailableQuantity < 5)
                {
                    alerts.Add(new StockAlert
                    {
                        ProductId = item.ProductId,
                        ProductName = product.Name,
                        AlertType = StockAlertType.LowStock,
                        Message = $"'{product.Name}' has low stock: {stockLevel.AvailableQuantity} units available.",
                        CurrentQuantity = stockLevel.AvailableQuantity
                    });
                }

                // Expiry alerts
                if (product.ExpiryDate.HasValue)
                {
                    if (product.ExpiryDate.Value < DateTime.UtcNow)
                    {
                        alerts.Add(new StockAlert
                        {
                            ProductId = item.ProductId,
                            ProductName = product.Name,
                            AlertType = StockAlertType.Expired,
                            Message = $"'{product.Name}' expired on {product.ExpiryDate.Value:yyyy-MM-dd}.",
                            ExpiryDate = product.ExpiryDate
                        });
                    }
                    else if (product.ExpiryDate.Value <= DateTime.UtcNow.AddDays(7))
                    {
                        alerts.Add(new StockAlert
                        {
                            ProductId = item.ProductId,
                            ProductName = product.Name,
                            AlertType = StockAlertType.NearExpiry,
                            Message = $"'{product.Name}' expires on {product.ExpiryDate.Value:yyyy-MM-dd}.",
                            ExpiryDate = product.ExpiryDate
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating stock alert for sale item {SaleItemId}", item.Id);
            }
        }

        return alerts;
    }

    // =========================================================================
    // Stock Reservation System
    // =========================================================================

    /// <summary>
    /// Reserves stock for a sale, reducing the available quantity for other concurrent sales.
    /// Requirement 7.3: Account for items already in active sales.
    /// </summary>
    public async Task<bool> ReserveStockAsync(Guid productId, int quantity, Guid saleId)
    {
        if (productId == Guid.Empty || saleId == Guid.Empty || quantity <= 0)
        {
            _logger.LogWarning("Invalid parameters for stock reservation: product={ProductId}, qty={Qty}, sale={SaleId}",
                productId, quantity, saleId);
            return false;
        }

        try
        {
            // Validate sufficient stock is available before reserving
            var stockLevel = await GetCurrentStockLevelAsync(productId);
            if (stockLevel.AvailableQuantity < quantity)
            {
                _logger.LogWarning(
                    "Cannot reserve {Quantity} units of product {ProductId} for sale {SaleId}: only {Available} available",
                    quantity, productId, saleId, stockLevel.AvailableQuantity);
                return false;
            }

            lock (_reservationLock)
            {
                if (!_reservations.TryGetValue(saleId, out var saleReservations))
                {
                    saleReservations = new List<StockReservation>();
                    _reservations[saleId] = saleReservations;
                }

                // Update existing reservation for this product in this sale, or add new one
                var existing = saleReservations.FirstOrDefault(r => r.ProductId == productId);
                if (existing != null)
                {
                    existing.ReservedQuantity += quantity;
                    existing.ReservedAt = DateTime.UtcNow;
                }
                else
                {
                    saleReservations.Add(new StockReservation
                    {
                        ProductId = productId,
                        SaleId = saleId,
                        ReservedQuantity = quantity,
                        ReservedAt = DateTime.UtcNow
                    });
                }
            }

            _logger.LogDebug("Reserved {Quantity} units of product {ProductId} for sale {SaleId}",
                quantity, productId, saleId);

            await Task.CompletedTask;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reserving stock for product {ProductId}, sale {SaleId}", productId, saleId);
            return false;
        }
    }

    /// <summary>
    /// Releases all stock reservations associated with a sale.
    /// Requirement 2.6: Proper cleanup when items are removed or sale is cancelled.
    /// </summary>
    public async Task<bool> ReleaseStockReservationAsync(Guid saleId)
    {
        if (saleId == Guid.Empty)
        {
            _logger.LogWarning("Cannot release stock reservation: sale ID is empty");
            return false;
        }

        try
        {
            lock (_reservationLock)
            {
                if (_reservations.ContainsKey(saleId))
                {
                    _reservations.Remove(saleId);
                    _logger.LogDebug("Released all stock reservations for sale {SaleId}", saleId);
                }
            }

            await Task.CompletedTask;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing stock reservation for sale {SaleId}", saleId);
            return false;
        }
    }

    // =========================================================================
    // Stock Level Queries
    // =========================================================================

    /// <summary>
    /// Gets the current stock level for a product, accounting for in-memory reservations
    /// and items in active sales stored in the database.
    /// </summary>
    public async Task<StockLevel> GetCurrentStockLevelAsync(Guid productId, Guid? shopId = null)
    {
        try
        {
            // Get physical stock from the database
            int physicalQty;
            if (shopId.HasValue)
            {
                var shopStock = await _stockRepository.FindAsync(s =>
                    s.ProductId == productId && s.ShopId == shopId.Value && !s.IsDeleted);
                physicalQty = shopStock.Sum(s => s.Quantity);
            }
            else
            {
                physicalQty = await _stockRepository.GetStockQuantityAsync(productId);
            }

            // Calculate reserved quantity from in-memory reservations
            int inMemoryReservedQty = 0;
            lock (_reservationLock)
            {
                inMemoryReservedQty = _reservations.Values
                    .SelectMany(r => r)
                    .Where(r => r.ProductId == productId)
                    .Sum(r => r.ReservedQuantity);
            }

            // Also account for items in active/draft sales in the database
            // (covers cases where reservations were not explicitly made, e.g., after restart)
            var activeSaleItems = await _saleItemRepository.FindAsync(si =>
                si.ProductId == productId && !si.IsDeleted);

            var activeSales = await _saleRepository.FindAsync(s =>
                (s.Status == SaleStatus.Active || s.Status == SaleStatus.Draft) && !s.IsDeleted);

            var activeSaleIds = activeSales.Select(s => s.Id).ToHashSet();
            var dbReservedQty = activeSaleItems
                .Where(si => activeSaleIds.Contains(si.SaleId))
                .Sum(si => si.Quantity);

            // Use the maximum of in-memory reservations and DB-based reservations to avoid double-counting
            var totalReserved = Math.Max(inMemoryReservedQty, dbReservedQty);

            return new StockLevel
            {
                ProductId = productId,
                PhysicalQuantity = physicalQty,
                ReservedQuantity = totalReserved,
                ShopId = shopId,
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting stock level for product {ProductId}", productId);
            return new StockLevel
            {
                ProductId = productId,
                PhysicalQuantity = 0,
                ReservedQuantity = 0,
                ShopId = shopId,
                LastUpdated = DateTime.UtcNow
            };
        }
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private class StockReservation
    {
        public Guid ProductId { get; set; }
        public Guid SaleId { get; set; }
        public int ReservedQuantity { get; set; }
        public DateTime ReservedAt { get; set; }
    }
}
