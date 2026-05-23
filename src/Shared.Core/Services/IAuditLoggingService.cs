using Shared.Core.DTOs;
using Shared.Core.Entities;
using Shared.Core.Enums;

namespace Shared.Core.Services;

/// <summary>
/// Service for recording and querying sale-specific audit events.
/// Satisfies Requirements 10.1 (timestamps), 10.2 (user info), 10.3 (change history),
/// and 10.6 (audit reports).
/// </summary>
public interface IAuditLoggingService
{
    /// <summary>
    /// Logs a sale-level event (creation, completion, cancellation, etc.).
    /// </summary>
    /// <param name="saleId">The sale being acted upon.</param>
    /// <param name="userId">The user performing the action (Requirement 10.2).</param>
    /// <param name="eventType">The type of event.</param>
    /// <param name="description">Human-readable description.</param>
    /// <param name="oldValues">Optional object representing the state before the change.</param>
    /// <param name="newValues">Optional object representing the state after the change.</param>
    /// <param name="deviceId">Optional device that originated the event.</param>
    Task LogSaleEventAsync(
        Guid saleId,
        Guid userId,
        SaleAuditEventType eventType,
        string description,
        object? oldValues = null,
        object? newValues = null,
        Guid? deviceId = null);

    /// <summary>
    /// Logs a change to a specific sale item (add, remove, quantity/weight update).
    /// Captures old and new values for full change history (Requirement 10.3).
    /// </summary>
    Task LogItemChangeAsync(
        Guid saleId,
        Guid saleItemId,
        Guid userId,
        SaleAuditEventType eventType,
        object? oldValues,
        object? newValues);

    /// <summary>Returns all audit logs for a specific sale, ordered by timestamp.</summary>
    Task<IEnumerable<SaleAuditLog>> GetAuditLogsForSaleAsync(Guid saleId);

    /// <summary>Returns audit logs created by a user within the given date range.</summary>
    Task<IEnumerable<SaleAuditLog>> GetAuditLogsByUserAsync(Guid userId, DateTime fromDate, DateTime toDate);

    /// <summary>Returns audit logs within a date range, optionally scoped to a shop.</summary>
    Task<IEnumerable<SaleAuditLog>> GetAuditLogsByDateRangeAsync(
        DateTime fromDate, DateTime toDate, Guid? shopId = null);

    /// <summary>
    /// Generates a summary audit report for management and compliance purposes (Requirement 10.6).
    /// </summary>
    Task<SaleAuditReport> GenerateAuditReportAsync(
        DateTime fromDate, DateTime toDate, Guid? shopId = null, Guid? userId = null);
}
