using Shared.Core.Entities;
using Shared.Core.Enums;

namespace Shared.Core.Services;

/// <summary>
/// Service for processing payments, validating payment methods, generating receipts,
/// and handling payment failures with state preservation.
///
/// Requirement 6.1: Validate the selected payment method when completing a sale.
/// Requirement 6.2: Calculate final totals including all taxes and discounts before completion.
/// Requirement 6.4: Generate and store receipt data for printing and records.
/// Requirement 6.5: Maintain sale state for retry without data loss when payment fails.
/// </summary>
public interface IPaymentProcessingService
{
    /// <summary>
    /// Validates that a payment method is available and the amount is sufficient.
    /// Requirement 6.1: Validate the selected payment method.
    /// </summary>
    /// <param name="paymentMethod">The payment method to validate</param>
    /// <param name="amount">The amount to be paid</param>
    /// <returns>Validation result indicating whether the payment method is valid</returns>
    Task<PaymentValidationResult> ValidatePaymentMethodAsync(PaymentMethod paymentMethod, decimal amount);

    /// <summary>
    /// Processes a payment for a sale, calculating final totals and completing the transaction.
    /// Requirement 6.2: Calculate final totals including all taxes and discounts before completion.
    /// Requirement 6.5: Maintain sale state for retry without data loss when payment fails.
    /// </summary>
    /// <param name="sale">The sale to process payment for</param>
    /// <param name="paymentMethod">The payment method to use</param>
    /// <param name="amountPaid">The amount paid by the customer</param>
    /// <returns>Payment result with transaction details and change amount</returns>
    Task<PaymentResult> ProcessPaymentAsync(Sale sale, PaymentMethod paymentMethod, decimal amountPaid);

    /// <summary>
    /// Processes a refund for a completed sale.
    /// </summary>
    /// <param name="saleId">The ID of the sale to refund</param>
    /// <param name="refundAmount">The amount to refund</param>
    /// <param name="reason">The reason for the refund</param>
    /// <returns>Refund result with transaction details</returns>
    Task<RefundResult> ProcessRefundAsync(Guid saleId, decimal refundAmount, string reason);

    /// <summary>
    /// Generates a receipt for a completed sale with full transaction data.
    /// Requirement 6.4: Generate and store receipt data for printing and records.
    /// </summary>
    /// <param name="sale">The completed sale</param>
    /// <param name="paymentResult">The payment result from processing</param>
    /// <returns>Payment receipt with complete transaction information</returns>
    Task<PaymentReceipt> GenerateReceiptAsync(Sale sale, PaymentResult paymentResult);

    /// <summary>
    /// Gets the available payment methods for a shop.
    /// </summary>
    /// <param name="shopId">The shop ID to get payment methods for</param>
    /// <returns>Collection of available payment methods</returns>
    Task<IEnumerable<PaymentMethod>> GetAvailablePaymentMethodsAsync(Guid shopId);
}

/// <summary>
/// Result of validating a payment method.
/// </summary>
public class PaymentValidationResult
{
    /// <summary>Whether the payment method is valid for use</summary>
    public bool IsValid { get; set; }

    /// <summary>The payment method that was validated</summary>
    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>The amount that was validated</summary>
    public decimal Amount { get; set; }

    /// <summary>Reason for validation failure, if any</summary>
    public string? ValidationMessage { get; set; }

    /// <summary>List of validation errors</summary>
    public List<string> ValidationErrors { get; set; } = new();

    /// <summary>Whether the payment method requires exact change</summary>
    public bool RequiresExactAmount { get; set; }

    /// <summary>Creates a successful validation result</summary>
    public static PaymentValidationResult Success(PaymentMethod method, decimal amount) =>
        new() { IsValid = true, PaymentMethod = method, Amount = amount };

    /// <summary>Creates a failed validation result</summary>
    public static PaymentValidationResult Failure(PaymentMethod method, decimal amount, string message) =>
        new()
        {
            IsValid = false,
            PaymentMethod = method,
            Amount = amount,
            ValidationMessage = message,
            ValidationErrors = new List<string> { message }
        };
}

/// <summary>
/// Result of processing a payment transaction.
/// </summary>
public class PaymentResult
{
    /// <summary>Whether the payment was processed successfully</summary>
    public bool IsSuccess { get; set; }

    /// <summary>Unique transaction identifier</summary>
    public Guid TransactionId { get; set; } = Guid.NewGuid();

    /// <summary>The sale ID this payment is for</summary>
    public Guid SaleId { get; set; }

    /// <summary>The payment method used</summary>
    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>The final total amount due</summary>
    public decimal FinalTotal { get; set; }

    /// <summary>The amount paid by the customer</summary>
    public decimal AmountPaid { get; set; }

    /// <summary>The change amount to return to the customer</summary>
    public decimal ChangeAmount { get; set; }

    /// <summary>Subtotal before discounts and taxes</summary>
    public decimal Subtotal { get; set; }

    /// <summary>Total discount amount applied</summary>
    public decimal TotalDiscount { get; set; }

    /// <summary>Membership discount amount applied</summary>
    public decimal MembershipDiscountAmount { get; set; }

    /// <summary>Total tax amount applied</summary>
    public decimal TotalTax { get; set; }

    /// <summary>Timestamp when the payment was processed</summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Error message if payment failed</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Whether the sale state was preserved after a failure</summary>
    public bool SaleStatePreserved { get; set; }

    /// <summary>The invoice number for this transaction</summary>
    public string InvoiceNumber { get; set; } = string.Empty;
}

/// <summary>
/// Result of processing a refund.
/// </summary>
public class RefundResult
{
    /// <summary>Whether the refund was processed successfully</summary>
    public bool IsSuccess { get; set; }

    /// <summary>Unique refund transaction identifier</summary>
    public Guid RefundTransactionId { get; set; } = Guid.NewGuid();

    /// <summary>The original sale ID</summary>
    public Guid OriginalSaleId { get; set; }

    /// <summary>The amount refunded</summary>
    public decimal RefundAmount { get; set; }

    /// <summary>The reason for the refund</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Timestamp when the refund was processed</summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Error message if refund failed</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Whether this is a partial refund</summary>
    public bool IsPartialRefund { get; set; }
}

/// <summary>
/// Complete receipt data for a payment transaction.
/// Requirement 6.4: Contains all required transaction information for printing and records.
/// </summary>
public class PaymentReceipt
{
    /// <summary>Unique receipt identifier</summary>
    public Guid ReceiptId { get; set; } = Guid.NewGuid();

    /// <summary>The sale invoice number</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>The transaction ID from payment processing</summary>
    public Guid TransactionId { get; set; }

    /// <summary>The sale ID</summary>
    public Guid SaleId { get; set; }

    /// <summary>Shop name for the receipt header</summary>
    public string ShopName { get; set; } = string.Empty;

    /// <summary>Shop address for the receipt header</summary>
    public string? ShopAddress { get; set; }

    /// <summary>Shop phone number for the receipt header</summary>
    public string? ShopPhone { get; set; }

    /// <summary>Customer name if applicable</summary>
    public string? CustomerName { get; set; }

    /// <summary>Customer membership number if applicable</summary>
    public string? MembershipNumber { get; set; }

    /// <summary>Line items on the receipt</summary>
    public List<ReceiptLineItem> LineItems { get; set; } = new();

    /// <summary>Subtotal before discounts and taxes</summary>
    public decimal Subtotal { get; set; }

    /// <summary>Total discount amount</summary>
    public decimal TotalDiscount { get; set; }

    /// <summary>Membership discount amount</summary>
    public decimal MembershipDiscountAmount { get; set; }

    /// <summary>Total tax amount</summary>
    public decimal TotalTax { get; set; }

    /// <summary>Final total amount due</summary>
    public decimal FinalTotal { get; set; }

    /// <summary>Amount paid by the customer</summary>
    public decimal AmountPaid { get; set; }

    /// <summary>Change amount returned to the customer</summary>
    public decimal ChangeAmount { get; set; }

    /// <summary>Payment method used</summary>
    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>Timestamp when the sale was completed</summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>Timestamp when the receipt was generated</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Applied discounts for the receipt</summary>
    public List<ReceiptDiscount> AppliedDiscounts { get; set; } = new();

    /// <summary>Footer message for the receipt</summary>
    public string FooterMessage { get; set; } = "Thank you for your business!";

    /// <summary>Whether the receipt data is complete and valid</summary>
    public bool IsComplete { get; set; }
}

/// <summary>
/// A line item on a payment receipt.
/// </summary>
public class ReceiptLineItem
{
    /// <summary>Product name</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Product code or barcode</summary>
    public string? ProductCode { get; set; }

    /// <summary>Quantity for regular items</summary>
    public int Quantity { get; set; }

    /// <summary>Weight for weight-based items</summary>
    public decimal? Weight { get; set; }

    /// <summary>Rate per kilogram for weight-based items</summary>
    public decimal? RatePerKilogram { get; set; }

    /// <summary>Unit price</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Line total</summary>
    public decimal LineTotal { get; set; }

    /// <summary>Batch number if applicable</summary>
    public string? BatchNumber { get; set; }

    /// <summary>Whether this is a weight-based item</summary>
    public bool IsWeightBased { get; set; }
}

/// <summary>
/// A discount entry on a payment receipt.
/// </summary>
public class ReceiptDiscount
{
    /// <summary>Discount name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Discount amount</summary>
    public decimal Amount { get; set; }

    /// <summary>Reason for the discount</summary>
    public string Reason { get; set; } = string.Empty;
}
