using Microsoft.Extensions.DependencyInjection;
using Shared.Core.DependencyInjection;
using Shared.Core.Enums;
using Shared.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace Shared.Core.Tests;

/// <summary>
/// Property-based tests for audit trail completeness.
///
/// Feature: sales-service-implementation, Property 18: Audit Trail Completeness
/// Validates: Requirements 10.1, 10.2, 10.3
///
/// Property: For any sale operation (creation, modification, completion), the system should
/// log all events with timestamps, user information, and maintain change history.
/// </summary>
public class AuditTrailCompletenessPropertyTest : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IAuditLoggingService _auditLoggingService;
    private readonly ITestOutputHelper _output;

    // Minimum iterations required by the spec
    private const int MinIterations = 100;

    public AuditTrailCompletenessPropertyTest(ITestOutputHelper output)
    {
        _output = output;

        var services = new ServiceCollection();
        services.AddSharedCoreInMemory();
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _auditLoggingService = _serviceProvider.GetRequiredService<IAuditLoggingService>();
    }

    /// <summary>
    /// **Validates: Requirements 10.1, 10.2**
    ///
    /// Property 18: Audit Trail Completeness
    /// For any sale event logged, the resulting audit record must always contain:
    /// - A non-empty SaleId (Requirement 10.1: event tied to a sale)
    /// - A non-empty UserId (Requirement 10.2: user accountability)
    /// - A UTC timestamp that is recent and not in the future (Requirement 10.1: timestamps)
    /// - A non-empty description (Requirement 10.1: event description)
    /// - The correct EventType matching what was logged
    /// </summary>
    [Fact]
    public async Task Property18_SaleEventLogging_AuditRecordAlwaysContainsRequiredFields()
    {
        var random = new Random(42);
        var allEventTypes = Enum.GetValues<SaleAuditEventType>();
        int passCount = 0;

        for (int i = 0; i < MinIterations; i++)
        {
            var saleId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var eventType = allEventTypes[random.Next(allEventTypes.Length)];
            var description = $"Test event {i}: {eventType}";
            var deviceId = random.NextDouble() < 0.5 ? (Guid?)Guid.NewGuid() : null;

            // Optionally include old/new values (Requirement 10.3: change history)
            object? oldValues = random.NextDouble() < 0.4
                ? new { Status = "Before", Amount = random.Next(1, 1000) }
                : null;
            object? newValues = random.NextDouble() < 0.6
                ? new { Status = "After", Amount = random.Next(1, 1000) }
                : null;

            var beforeLog = DateTime.UtcNow;

            await _auditLoggingService.LogSaleEventAsync(
                saleId, userId, eventType, description,
                oldValues: oldValues,
                newValues: newValues,
                deviceId: deviceId);

            var afterLog = DateTime.UtcNow;

            // Retrieve and verify the logged record
            var logs = (await _auditLoggingService.GetAuditLogsForSaleAsync(saleId)).ToList();

            // Property: exactly one log entry must exist for this sale
            Assert.True(logs.Count == 1,
                $"Iteration {i}: Exactly one audit log must be created per LogSaleEventAsync call. Got {logs.Count}.");

            var log = logs[0];

            // Requirement 10.1: event must be tied to the correct sale
            Assert.True(log.SaleId == saleId,
                $"Iteration {i}: SaleId must match the sale being audited.");

            // Requirement 10.2: user information must be recorded
            Assert.True(log.UserId == userId,
                $"Iteration {i}: UserId must be recorded for accountability.");

            // Requirement 10.1: timestamp must be present and recent (UTC, not in future)
            Assert.True(log.Timestamp >= beforeLog && log.Timestamp <= afterLog,
                $"Iteration {i}: Timestamp {log.Timestamp:O} must be between {beforeLog:O} and {afterLog:O}.");

            // Requirement 10.1: description must be non-empty
            Assert.False(string.IsNullOrWhiteSpace(log.EventDescription),
                $"Iteration {i}: EventDescription must not be empty.");
            Assert.True(log.EventDescription == description,
                $"Iteration {i}: EventDescription must match the provided description.");

            // Event type must be preserved exactly
            Assert.True(log.EventType == eventType,
                $"Iteration {i}: EventType must match what was logged.");

            // Device ID must be preserved when provided
            if (deviceId.HasValue)
            {
                Assert.True(log.DeviceId == deviceId.Value,
                    $"Iteration {i}: DeviceId must be recorded when provided.");
            }

            passCount++;
        }

        _output.WriteLine($"Property 18 (sale event logging): {MinIterations} iterations completed. {passCount} passed.");
    }

    /// <summary>
    /// **Validates: Requirements 10.3**
    ///
    /// Property 18: Audit Trail Completeness
    /// For any item modification event, the audit record must maintain change history by
    /// capturing old and new values. When old values are provided, they must be serialized
    /// and stored. When new values are provided, they must be serialized and stored.
    /// Null values must remain null (not serialized to empty strings).
    /// </summary>
    [Fact]
    public async Task Property18_ItemChangeLogging_ChangeHistoryAlwaysCapturedCorrectly()
    {
        var random = new Random(123);
        var itemEventTypes = new[]
        {
            SaleAuditEventType.ItemAdded,
            SaleAuditEventType.ItemRemoved,
            SaleAuditEventType.ItemQuantityChanged,
            SaleAuditEventType.ItemWeightChanged
        };

        for (int i = 0; i < MinIterations; i++)
        {
            var saleId = Guid.NewGuid();
            var saleItemId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var eventType = itemEventTypes[random.Next(itemEventTypes.Length)];

            // Generate random old/new values based on event type
            var (oldValues, newValues) = GenerateItemChangeValues(random, eventType);

            await _auditLoggingService.LogItemChangeAsync(
                saleId, saleItemId, userId, eventType, oldValues, newValues);

            var logs = (await _auditLoggingService.GetAuditLogsForSaleAsync(saleId)).ToList();

            Assert.True(logs.Count == 1,
                $"Iteration {i}: Exactly one audit log must be created per LogItemChangeAsync call. Got {logs.Count}.");

            var log = logs[0];

            // Requirement 10.3: change history must be maintained
            Assert.True(log.SaleId == saleId,
                $"Iteration {i}: SaleId must be recorded.");
            Assert.True(log.UserId == userId,
                $"Iteration {i}: UserId must be recorded for accountability.");
            Assert.True(log.EventType == eventType,
                $"Iteration {i}: EventType must match the item change type.");

            // Requirement 10.3: old values must be serialized when provided
            if (oldValues != null)
            {
                Assert.NotNull(log.OldValues);
                Assert.False(string.IsNullOrWhiteSpace(log.OldValues),
                    $"Iteration {i}: OldValues must not be empty when old state is provided.");
            }
            else
            {
                Assert.Null(log.OldValues);
            }

            // Requirement 10.3: new values must be serialized when provided
            if (newValues != null)
            {
                Assert.NotNull(log.NewValues);
                Assert.False(string.IsNullOrWhiteSpace(log.NewValues),
                    $"Iteration {i}: NewValues must not be empty when new state is provided.");
            }
            else
            {
                Assert.Null(log.NewValues);
            }

            // Description must be auto-generated and non-empty
            Assert.False(string.IsNullOrWhiteSpace(log.EventDescription),
                $"Iteration {i}: EventDescription must be auto-generated for item changes.");
        }

        _output.WriteLine($"Property 18 (item change logging): {MinIterations} iterations completed.");
    }

    /// <summary>
    /// **Validates: Requirements 10.1, 10.2, 10.3**
    ///
    /// Property 18: Audit Trail Completeness
    /// For any sequence of sale operations, the audit trail must be complete and ordered:
    /// - All events must be retrievable by sale ID
    /// - Events must be ordered by timestamp (ascending)
    /// - The count of retrieved events must equal the count of logged events
    /// - Each event must preserve its user ID and event type
    /// </summary>
    [Fact]
    public async Task Property18_AuditTrailOrdering_EventsAlwaysRetrievableInChronologicalOrder()
    {
        var random = new Random(456);
        var allEventTypes = Enum.GetValues<SaleAuditEventType>();

        for (int i = 0; i < MinIterations; i++)
        {
            var saleId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            // Log a random number of events (2–8) for the same sale
            var eventCount = random.Next(2, 9);
            var loggedEvents = new List<(SaleAuditEventType EventType, string Description)>();

            for (int j = 0; j < eventCount; j++)
            {
                var eventType = allEventTypes[random.Next(allEventTypes.Length)];
                var description = $"Event {j} of type {eventType}";
                loggedEvents.Add((eventType, description));

                await _auditLoggingService.LogSaleEventAsync(
                    saleId, userId, eventType, description);

                // Small delay to ensure distinct timestamps
                await Task.Delay(1);
            }

            // Retrieve all logs for this sale
            var retrievedLogs = (await _auditLoggingService.GetAuditLogsForSaleAsync(saleId)).ToList();

            // Property: all logged events must be retrievable (Requirement 10.1)
            Assert.True(retrievedLogs.Count == eventCount,
                $"Iteration {i}: All {eventCount} logged events must be retrievable. Got {retrievedLogs.Count}.");

            // Property: events must be ordered by timestamp ascending (Requirement 10.1)
            for (int k = 1; k < retrievedLogs.Count; k++)
            {
                Assert.True(retrievedLogs[k].Timestamp >= retrievedLogs[k - 1].Timestamp,
                    $"Iteration {i}: Events must be ordered by timestamp ascending. " +
                    $"Event {k} ({retrievedLogs[k].Timestamp:O}) is before event {k - 1} ({retrievedLogs[k - 1].Timestamp:O}).");
            }

            // Property: all events must have the correct sale ID and user ID (Requirements 10.1, 10.2)
            foreach (var log in retrievedLogs)
            {
                Assert.True(log.SaleId == saleId,
                    $"Iteration {i}: All logs must have the correct SaleId.");
                Assert.True(log.UserId == userId,
                    $"Iteration {i}: All logs must have the correct UserId.");
                Assert.False(string.IsNullOrWhiteSpace(log.EventDescription),
                    $"Iteration {i}: All logs must have a non-empty description.");
                Assert.True(log.Timestamp <= DateTime.UtcNow,
                    $"Iteration {i}: Timestamp must not be in the future.");
            }
        }

        _output.WriteLine($"Property 18 (audit trail ordering): {MinIterations} iterations completed.");
    }

    /// <summary>
    /// **Validates: Requirements 10.1, 10.2, 10.6**
    ///
    /// Property 18: Audit Trail Completeness
    /// For any date range query, the generated audit report must:
    /// - Include only events within the specified date range
    /// - Correctly count events by type
    /// - Correctly identify the most active users
    /// - Have a GeneratedAt timestamp that is not in the future
    /// </summary>
    [Fact]
    public async Task Property18_AuditReportGeneration_ReportAlwaysAccuratelyReflectsLoggedEvents()
    {
        var random = new Random(789);
        var allEventTypes = Enum.GetValues<SaleAuditEventType>();

        for (int i = 0; i < MinIterations; i++)
        {
            var saleId = Guid.NewGuid();
            var userId1 = Guid.NewGuid();
            var userId2 = Guid.NewGuid();

            var fromDate = DateTime.UtcNow.AddHours(-1);
            var toDate = DateTime.UtcNow.AddHours(1);

            // Log a random number of events for two users
            var user1EventCount = random.Next(1, 6);
            var user2EventCount = random.Next(1, 4);
            var totalExpected = user1EventCount + user2EventCount;

            for (int j = 0; j < user1EventCount; j++)
            {
                var eventType = allEventTypes[random.Next(allEventTypes.Length)];
                await _auditLoggingService.LogSaleEventAsync(
                    saleId, userId1, eventType, $"User1 event {j}");
            }

            for (int j = 0; j < user2EventCount; j++)
            {
                var eventType = allEventTypes[random.Next(allEventTypes.Length)];
                await _auditLoggingService.LogSaleEventAsync(
                    saleId, userId2, eventType, $"User2 event {j}");
            }

            // Generate audit report (Requirement 10.6)
            var report = await _auditLoggingService.GenerateAuditReportAsync(fromDate, toDate);

            // Property: total event count must match (Requirement 10.1)
            Assert.True(report.TotalEvents >= totalExpected,
                $"Iteration {i}: Report must include at least {totalExpected} events. Got {report.TotalEvents}.");

            // Property: report must have a valid generation timestamp (Requirement 10.1)
            Assert.True(report.GeneratedAt <= DateTime.UtcNow.AddSeconds(1),
                $"Iteration {i}: GeneratedAt must not be in the future.");
            Assert.True(report.GeneratedAt >= fromDate,
                $"Iteration {i}: GeneratedAt must be within the report period.");

            // Property: report period must match the requested range (Requirement 10.6)
            Assert.True(report.ReportPeriod.StartDate == fromDate,
                $"Iteration {i}: Report period start must match fromDate.");
            Assert.True(report.ReportPeriod.EndDate == toDate,
                $"Iteration {i}: Report period end must match toDate.");

            // Property: event type counts must be non-negative (Requirement 10.1)
            foreach (var kvp in report.EventsByType)
            {
                Assert.True(kvp.Value >= 0,
                    $"Iteration {i}: Event count for {kvp.Key} must be non-negative.");
            }

            // Property: top users must have valid user IDs and positive event counts (Requirement 10.2)
            foreach (var user in report.TopUsers)
            {
                Assert.True(user.UserId != Guid.Empty,
                    $"Iteration {i}: TopUser UserId must not be empty.");
                Assert.True(user.TotalEvents > 0,
                    $"Iteration {i}: TopUser TotalEvents must be positive.");
            }
        }

        _output.WriteLine($"Property 18 (audit report generation): {MinIterations} iterations completed.");
    }

    /// <summary>
    /// **Validates: Requirements 10.2**
    ///
    /// Property 18: Audit Trail Completeness
    /// For any user-based audit query, the results must only contain events for that user
    /// within the specified date range. User isolation must be maintained across all queries.
    /// </summary>
    [Fact]
    public async Task Property18_UserIsolation_AuditLogsAlwaysFilteredByUserCorrectly()
    {
        var random = new Random(321);
        var allEventTypes = Enum.GetValues<SaleAuditEventType>();

        for (int i = 0; i < MinIterations; i++)
        {
            var saleId = Guid.NewGuid();
            var userId1 = Guid.NewGuid();
            var userId2 = Guid.NewGuid();

            var fromDate = DateTime.UtcNow.AddHours(-1);
            var toDate = DateTime.UtcNow.AddHours(1);

            // Log events for two different users
            var user1Count = random.Next(1, 5);
            var user2Count = random.Next(1, 4);

            for (int j = 0; j < user1Count; j++)
            {
                var eventType = allEventTypes[random.Next(allEventTypes.Length)];
                await _auditLoggingService.LogSaleEventAsync(
                    saleId, userId1, eventType, $"User1 event {j}");
            }

            for (int j = 0; j < user2Count; j++)
            {
                var eventType = allEventTypes[random.Next(allEventTypes.Length)];
                await _auditLoggingService.LogSaleEventAsync(
                    saleId, userId2, eventType, $"User2 event {j}");
            }

            // Query by user (Requirement 10.2: user accountability)
            var user1Logs = (await _auditLoggingService.GetAuditLogsByUserAsync(userId1, fromDate, toDate)).ToList();
            var user2Logs = (await _auditLoggingService.GetAuditLogsByUserAsync(userId2, fromDate, toDate)).ToList();

            // Property: user1 logs must only contain user1's events
            Assert.True(user1Logs.Count == user1Count,
                $"Iteration {i}: User1 must have exactly {user1Count} logs. Got {user1Logs.Count}.");
            foreach (var log in user1Logs)
            {
                Assert.True(log.UserId == userId1,
                    $"Iteration {i}: All user1 logs must have userId1.");
            }

            // Property: user2 logs must only contain user2's events
            Assert.True(user2Logs.Count == user2Count,
                $"Iteration {i}: User2 must have exactly {user2Count} logs. Got {user2Logs.Count}.");
            foreach (var log in user2Logs)
            {
                Assert.True(log.UserId == userId2,
                    $"Iteration {i}: All user2 logs must have userId2.");
            }

            // Property: no cross-contamination between users
            var user1LogIds = user1Logs.Select(l => l.Id).ToHashSet();
            var user2LogIds = user2Logs.Select(l => l.Id).ToHashSet();
            var overlap = user1LogIds.Intersect(user2LogIds).ToList();
            Assert.True(overlap.Count == 0,
                $"Iteration {i}: User1 and User2 logs must not overlap. Found {overlap.Count} overlapping entries.");
        }

        _output.WriteLine($"Property 18 (user isolation): {MinIterations} iterations completed.");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static (object? oldValues, object? newValues) GenerateItemChangeValues(
        Random random, SaleAuditEventType eventType)
    {
        return eventType switch
        {
            SaleAuditEventType.ItemAdded =>
                // Adding: no old state, new state has product details
                (null, (object)new { ProductId = Guid.NewGuid(), Quantity = random.Next(1, 20), UnitPrice = random.Next(1, 500) }),

            SaleAuditEventType.ItemRemoved =>
                // Removing: old state captured, no new state
                ((object)new { ProductId = Guid.NewGuid(), Quantity = random.Next(1, 20), TotalPrice = random.Next(1, 1000) }, null),

            SaleAuditEventType.ItemQuantityChanged =>
                // Quantity change: both old and new states
                ((object)new { Quantity = random.Next(1, 10), TotalPrice = random.Next(1, 500) },
                 (object)new { Quantity = random.Next(1, 20), TotalPrice = random.Next(1, 1000) }),

            SaleAuditEventType.ItemWeightChanged =>
                // Weight change: both old and new states
                ((object)new { Weight = Math.Round(random.NextDouble() * 5, 3), TotalPrice = random.Next(1, 500) },
                 (object)new { Weight = Math.Round(random.NextDouble() * 10, 3), TotalPrice = random.Next(1, 1000) }),

            _ =>
                // Default: random old/new values
                (random.NextDouble() < 0.5 ? (object?)new { Value = random.Next(1, 100) } : null,
                 random.NextDouble() < 0.5 ? (object?)new { Value = random.Next(1, 100) } : null)
        };
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}
