using Microsoft.Extensions.Logging;
using Shared.Core.DTOs;
using Shared.Core.Entities;
using Shared.Core.Enums;
using Shared.Core.Repositories;
using System.Text.Json;

namespace Shared.Core.Services;

/// <summary>
/// Production implementation of <see cref="IAuditLoggingService"/>.
/// Persists sale audit events to <see cref="SaleAuditLog"/> via <see cref="ISaleAuditLogRepository"/>
/// and provides querying and report generation for compliance (Requirements 10.1–10.3, 10.6).
/// </summary>
public class AuditLoggingService : IAuditLoggingService
{
    private readonly ISaleAuditLogRepository _repository;
    private readonly ILogger<AuditLoggingService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public AuditLoggingService(
        ISaleAuditLogRepository repository,
        ILogger<AuditLoggingService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // =========================================================================
    // Logging
    // =========================================================================

    /// <inheritdoc/>
    public async Task LogSaleEventAsync(
        Guid saleId,
        Guid userId,
        SaleAuditEventType eventType,
        string description,
        object? oldValues = null,
        object? newValues = null,
        Guid? deviceId = null)
    {
        try
        {
            var entry = new SaleAuditLog
            {
                Id = Guid.NewGuid(),
                SaleId = saleId,
                UserId = userId,
                EventType = eventType,
                EventDescription = description,
                OldValues = Serialize(oldValues),
                NewValues = Serialize(newValues),
                Timestamp = DateTime.UtcNow,
                DeviceId = deviceId
            };

            await _repository.AddAsync(entry);
            await _repository.SaveChangesAsync();

            _logger.LogInformation(
                "Audit: {EventType} on sale {SaleId} by user {UserId} — {Description}",
                eventType, saleId, userId, description);
        }
        catch (Exception ex)
        {
            // Audit failures must not block the primary operation (design doc: "continue operation but alert")
            _logger.LogError(ex,
                "Failed to log audit event {EventType} for sale {SaleId}", eventType, saleId);
        }
    }

    /// <inheritdoc/>
    public async Task LogItemChangeAsync(
        Guid saleId,
        Guid saleItemId,
        Guid userId,
        SaleAuditEventType eventType,
        object? oldValues,
        object? newValues)
    {
        var description = eventType switch
        {
            SaleAuditEventType.ItemAdded => $"Item {saleItemId} added to sale",
            SaleAuditEventType.ItemRemoved => $"Item {saleItemId} removed from sale",
            SaleAuditEventType.ItemQuantityChanged => $"Item {saleItemId} quantity changed",
            SaleAuditEventType.ItemWeightChanged => $"Item {saleItemId} weight changed",
            _ => $"Item {saleItemId} changed ({eventType})"
        };

        await LogSaleEventAsync(saleId, userId, eventType, description, oldValues, newValues);
    }

    // =========================================================================
    // Querying
    // =========================================================================

    /// <inheritdoc/>
    public async Task<IEnumerable<SaleAuditLog>> GetAuditLogsForSaleAsync(Guid saleId)
    {
        if (saleId == Guid.Empty)
            throw new ArgumentException("Sale ID cannot be empty.", nameof(saleId));

        return await _repository.GetBySaleIdAsync(saleId);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<SaleAuditLog>> GetAuditLogsByUserAsync(
        Guid userId, DateTime fromDate, DateTime toDate)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));

        if (fromDate > toDate)
            throw new ArgumentException("fromDate must be before or equal to toDate.");

        return await _repository.GetByUserIdAsync(userId, fromDate, toDate);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<SaleAuditLog>> GetAuditLogsByDateRangeAsync(
        DateTime fromDate, DateTime toDate, Guid? shopId = null)
    {
        if (fromDate > toDate)
            throw new ArgumentException("fromDate must be before or equal to toDate.");

        return await _repository.GetByDateRangeAsync(fromDate, toDate, shopId);
    }

    // =========================================================================
    // Report Generation (Requirement 10.6)
    // =========================================================================

    /// <inheritdoc/>
    public async Task<SaleAuditReport> GenerateAuditReportAsync(
        DateTime fromDate, DateTime toDate, Guid? shopId = null, Guid? userId = null)
    {
        if (fromDate > toDate)
            throw new ArgumentException("fromDate must be before or equal to toDate.");

        var logs = (await _repository.GetByDateRangeAsync(fromDate, toDate, shopId)).ToList();

        // Apply optional user filter
        if (userId.HasValue)
            logs = logs.Where(l => l.UserId == userId.Value).ToList();

        // Event counts by type
        var eventsByType = logs
            .GroupBy(l => l.EventType)
            .ToDictionary(g => g.Key, g => g.Count());

        // Top users by activity
        var topUsers = logs
            .GroupBy(l => l.UserId)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new UserActivitySummary
            {
                UserId = g.Key,
                TotalEvents = g.Count(),
                EventsByType = g.GroupBy(l => l.EventType)
                                .ToDictionary(eg => eg.Key, eg => eg.Count())
            });

        // Sales summary
        var summary = new AuditSalesSummary
        {
            SalesCreated = eventsByType.GetValueOrDefault(SaleAuditEventType.SaleCreated),
            SalesCompleted = eventsByType.GetValueOrDefault(SaleAuditEventType.SaleCompleted),
            SalesCancelled = eventsByType.GetValueOrDefault(SaleAuditEventType.SaleCancelled),
            ItemsAdded = eventsByType.GetValueOrDefault(SaleAuditEventType.ItemAdded),
            ItemsRemoved = eventsByType.GetValueOrDefault(SaleAuditEventType.ItemRemoved),
            DiscountsApplied = eventsByType.GetValueOrDefault(SaleAuditEventType.DiscountApplied)
        };

        _logger.LogInformation(
            "Generated audit report for {From} – {To}: {TotalEvents} events",
            fromDate, toDate, logs.Count);

        return new SaleAuditReport
        {
            ReportPeriod = new DateRange { StartDate = fromDate, EndDate = toDate },
            TotalEvents = logs.Count,
            EventsByType = eventsByType,
            TopUsers = topUsers,
            SalesSummary = summary,
            GeneratedAt = DateTime.UtcNow
        };
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static string? Serialize(object? value)
    {
        if (value is null) return null;
        if (value is string s) return s;

        try
        {
            return JsonSerializer.Serialize(value, _jsonOptions);
        }
        catch
        {
            return value.ToString();
        }
    }
}
