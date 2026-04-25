using Shared.Core.Entities;

namespace Shared.Core.Services;

/// <summary>
/// Service for updating inventory after sale completion, managing customer purchase history,
/// and handling rollback for failed transactions.
///
/// Requirement 6.3: THE Inventory_Updater SHALL reduce stock levels for all sold items upon sale completion.
/// Requirement 6.6: THE Sale_Service SHALL update customer purchase history and membership points upon completion.
/// </summary>
public interface IInventoryUpdater
{
    /// <summary>
    /// Reduces stock levels for all items in a completed sale.
    /// Requirement 6.3: Reduce stock levels for all sold items upon sale completion.
    /// </summary>
    /// <param name="sale">The completed sale whose items should reduce stock</param>
    /// <returns>Result indicating success or failure with details</returns>
    Task<InventoryUpdateResult> ReduceStockLevelsAsync(Sale sale);

    /// <summary>
    /// Updates the customer's purchase history and membership points after a completed sale.
    /// Requirement 6.6: Update customer purchase history and membership points upon completion.
    /// </summary>
    /// <param name="sale">The completed sale to record in customer history</param>
    /// <returns>Result indicating success or failure with details</returns>
    Task<CustomerHistoryUpdateResult> UpdateCustomerPurchaseHistoryAsync(Sale sale);

    /// <summary>
    /// Rolls back inventory changes made for a sale (e.g., when a transaction fails after stock was reduced).
    /// Restores stock levels to their pre-sale state.
    /// </summary>
    /// <param name="saleId">The ID of the sale whose inventory changes should be rolled back</param>
    /// <returns>Result indicating success or failure with details</returns>
    Task<InventoryRollbackResult> RollbackInventoryUpdateAsync(Guid saleId);

    /// <summary>
    /// Performs a complete inventory update for a sale: reduces stock and updates customer history.
    /// Uses optimistic concurrency to handle concurrent sales safely.
    /// Requirement 6.3 + 6.6: Combined update operation with rollback on failure.
    /// </summary>
    /// <param name="sale">The completed sale to process</param>
    /// <returns>Result indicating success or failure with details</returns>
    Task<InventoryUpdateResult> ProcessSaleInventoryUpdateAsync(Sale sale);
}

/// <summary>
/// Result of an inventory stock level update operation.
/// </summary>
public class InventoryUpdateResult
{
    /// <summary>Whether the inventory update succeeded</summary>
    public bool IsSuccess { get; set; }

    /// <summary>The sale ID that was processed</summary>
    public Guid SaleId { get; set; }

    /// <summary>Number of items whose stock was successfully reduced</summary>
    public int ItemsUpdated { get; set; }

    /// <summary>Details of each item update</summary>
    public List<StockUpdateDetail> UpdatedItems { get; set; } = new();

    /// <summary>Error message if the update failed</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Whether a rollback was performed after a failure</summary>
    public bool WasRolledBack { get; set; }

    /// <summary>Creates a successful result</summary>
    public static InventoryUpdateResult Success(Guid saleId, int itemsUpdated, List<StockUpdateDetail> details) =>
        new() { IsSuccess = true, SaleId = saleId, ItemsUpdated = itemsUpdated, UpdatedItems = details };

    /// <summary>Creates a failed result</summary>
    public static InventoryUpdateResult Failure(Guid saleId, string errorMessage) =>
        new() { IsSuccess = false, SaleId = saleId, ErrorMessage = errorMessage };
}

/// <summary>
/// Details of a single stock update for one product.
/// </summary>
public class StockUpdateDetail
{
    /// <summary>The product whose stock was updated</summary>
    public Guid ProductId { get; set; }

    /// <summary>Product name for logging/display</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Quantity that was deducted from stock</summary>
    public int QuantityDeducted { get; set; }

    /// <summary>Stock level before the deduction</summary>
    public int StockBefore { get; set; }

    /// <summary>Stock level after the deduction</summary>
    public int StockAfter { get; set; }

    /// <summary>Whether this item's stock update succeeded</summary>
    public bool IsSuccess { get; set; }

    /// <summary>Error message if this item's update failed</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result of updating a customer's purchase history.
/// </summary>
public class CustomerHistoryUpdateResult
{
    /// <summary>Whether the customer history update succeeded</summary>
    public bool IsSuccess { get; set; }

    /// <summary>The sale ID that was processed</summary>
    public Guid SaleId { get; set; }

    /// <summary>The customer ID that was updated (null if no customer on sale)</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Whether the customer's membership tier was upgraded</summary>
    public bool TierUpgraded { get; set; }

    /// <summary>The new membership tier if upgraded</summary>
    public string? NewTier { get; set; }

    /// <summary>Total amount added to customer's spending history</summary>
    public decimal AmountAdded { get; set; }

    /// <summary>Error message if the update failed</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Whether the sale had no customer (skipped update)</summary>
    public bool NoCustomer { get; set; }

    /// <summary>Creates a successful result</summary>
    public static CustomerHistoryUpdateResult Success(Guid saleId, Guid customerId, decimal amountAdded) =>
        new() { IsSuccess = true, SaleId = saleId, CustomerId = customerId, AmountAdded = amountAdded };

    /// <summary>Creates a no-customer result (not an error)</summary>
    public static CustomerHistoryUpdateResult NoCustomerResult(Guid saleId) =>
        new() { IsSuccess = true, SaleId = saleId, NoCustomer = true };

    /// <summary>Creates a failed result</summary>
    public static CustomerHistoryUpdateResult Failure(Guid saleId, string errorMessage) =>
        new() { IsSuccess = false, SaleId = saleId, ErrorMessage = errorMessage };
}

/// <summary>
/// Result of rolling back inventory changes for a sale.
/// </summary>
public class InventoryRollbackResult
{
    /// <summary>Whether the rollback succeeded</summary>
    public bool IsSuccess { get; set; }

    /// <summary>The sale ID that was rolled back</summary>
    public Guid SaleId { get; set; }

    /// <summary>Number of items whose stock was restored</summary>
    public int ItemsRestored { get; set; }

    /// <summary>Error message if the rollback failed</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Creates a successful result</summary>
    public static InventoryRollbackResult Success(Guid saleId, int itemsRestored) =>
        new() { IsSuccess = true, SaleId = saleId, ItemsRestored = itemsRestored };

    /// <summary>Creates a failed result</summary>
    public static InventoryRollbackResult Failure(Guid saleId, string errorMessage) =>
        new() { IsSuccess = false, SaleId = saleId, ErrorMessage = errorMessage };
}
