namespace Shared.Core.Enums;

/// <summary>
/// Categorises the type of discount applied to a sale for audit and reporting purposes
/// </summary>
public enum SaleDiscountType
{
    /// <summary>
    /// Discount calculated as a percentage of the subtotal
    /// </summary>
    Percentage = 0,

    /// <summary>
    /// Discount applied as a fixed monetary amount
    /// </summary>
    FixedAmount = 1,

    /// <summary>
    /// Discount granted due to customer membership tier benefits
    /// </summary>
    Membership = 2,

    /// <summary>
    /// Discount from a time-limited or quantity-limited promotional campaign
    /// </summary>
    Promotional = 3
}
