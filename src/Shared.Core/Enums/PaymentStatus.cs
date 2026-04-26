namespace Shared.Core.Enums;

/// <summary>
/// Represents the processing status of a payment record
/// </summary>
public enum PaymentStatus
{
    /// <summary>
    /// Payment has been initiated but not yet processed
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Payment was successfully processed
    /// </summary>
    Completed = 1,

    /// <summary>
    /// Payment processing failed (see FailureReason)
    /// </summary>
    Failed = 2,

    /// <summary>
    /// Payment was refunded after completion
    /// </summary>
    Refunded = 3
}
