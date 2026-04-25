namespace Shared.Core.Enums;

/// <summary>
/// Represents the lifecycle status of a sale transaction
/// </summary>
public enum SaleStatus
{
    /// <summary>
    /// Sale has been created but not yet active (initial state)
    /// </summary>
    Draft = 0,

    /// <summary>
    /// Sale is actively being processed (items being added/modified)
    /// </summary>
    Active = 1,

    /// <summary>
    /// Sale has been successfully completed and payment received
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Sale was cancelled before completion
    /// </summary>
    Cancelled = 3,

    /// <summary>
    /// Sale has been refunded after completion
    /// </summary>
    Refunded = 4
}
