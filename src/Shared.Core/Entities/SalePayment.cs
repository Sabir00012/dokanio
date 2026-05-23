using Shared.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace Shared.Core.Entities;

/// <summary>
/// Records a payment transaction for a sale, supporting multiple payment methods
/// and providing a full audit trail of payment activity.
/// </summary>
public class SalePayment : ISoftDeletable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // --- Foreign key ---

    public Guid SaleId { get; set; }

    // --- Payment details ---

    /// <summary>
    /// Payment method used for this payment (Cash, Card, DigitalPayment, etc.)
    /// </summary>
    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>
    /// Amount allocated to this payment record
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal Amount { get; set; }

    /// <summary>
    /// Amount physically tendered by the customer (may exceed Amount for cash payments)
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal AmountTendered { get; set; }

    /// <summary>
    /// Change returned to the customer (AmountTendered - Amount)
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal ChangeAmount { get; set; }

    // --- Status ---

    /// <summary>
    /// Current status of this payment record
    /// </summary>
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    /// <summary>
    /// Timestamp when the payment was successfully processed
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Reason for payment failure (populated when Status = Failed)
    /// </summary>
    [MaxLength(500)]
    public string? FailureReason { get; set; }

    /// <summary>
    /// External reference number for card/digital payments (e.g., transaction ID, approval code)
    /// </summary>
    [MaxLength(100)]
    public string? ReferenceNumber { get; set; }

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
}
