using Shared.Core.Enums;

namespace Shared.Core.DTOs;

/// <summary>
/// Summary report of sale audit events for a given period (Requirement 10.6).
/// </summary>
public class SaleAuditReport
{
    /// <summary>The date range covered by this report.</summary>
    public DateRange ReportPeriod { get; set; } = new();

    /// <summary>Total number of audit events in the period.</summary>
    public int TotalEvents { get; set; }

    /// <summary>Breakdown of event counts by event type.</summary>
    public Dictionary<SaleAuditEventType, int> EventsByType { get; set; } = new();

    /// <summary>Most active users during the period.</summary>
    public IEnumerable<UserActivitySummary> TopUsers { get; set; } = Enumerable.Empty<UserActivitySummary>();

    /// <summary>High-level sales summary derived from audit events.</summary>
    public AuditSalesSummary SalesSummary { get; set; } = new();

    /// <summary>UTC timestamp when this report was generated.</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Activity summary for a single user within an audit report.
/// </summary>
public class UserActivitySummary
{
    public Guid UserId { get; set; }
    public int TotalEvents { get; set; }
    public Dictionary<SaleAuditEventType, int> EventsByType { get; set; } = new();
}

/// <summary>
/// High-level sales statistics derived from audit events.
/// </summary>
public class AuditSalesSummary
{
    public int SalesCreated { get; set; }
    public int SalesCompleted { get; set; }
    public int SalesCancelled { get; set; }
    public int ItemsAdded { get; set; }
    public int ItemsRemoved { get; set; }
    public int DiscountsApplied { get; set; }
}
