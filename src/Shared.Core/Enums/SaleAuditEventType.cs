namespace Shared.Core.Enums;

/// <summary>
/// Defines the types of audit events that can occur during sale operations.
/// Used by the AuditLoggingService to categorise and query sale-specific audit records.
/// </summary>
public enum SaleAuditEventType
{
    SaleCreated = 0,
    SaleCompleted = 1,
    SaleCancelled = 2,
    ItemAdded = 3,
    ItemRemoved = 4,
    ItemQuantityChanged = 5,
    ItemWeightChanged = 6,
    DiscountApplied = 7,
    DiscountRemoved = 8,
    PaymentMethodChanged = 9,
    CustomerAssigned = 10,
    CustomerRemoved = 11
}
