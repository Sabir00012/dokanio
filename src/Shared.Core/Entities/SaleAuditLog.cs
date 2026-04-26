using Shared.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace Shared.Core.Entities;

/// <summary>
/// Stores a single audit event for a sale operation.
/// Captures who did what, when, and what changed — supporting Requirements 10.1, 10.2, 10.3.
/// </summary>
public class SaleAuditLog : ISoftDeletable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The sale this event belongs to.</summary>
    [Required]
    public Guid SaleId { get; set; }

    /// <summary>The user who performed the operation (Requirement 10.2).</summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>Categorises the type of change (Requirement 10.1, 10.3).</summary>
    public SaleAuditEventType EventType { get; set; }

    /// <summary>Human-readable description of the event.</summary>
    [Required]
    [MaxLength(500)]
    public string EventDescription { get; set; } = string.Empty;

    /// <summary>JSON snapshot of the entity state before the change (Requirement 10.3).</summary>
    public string? OldValues { get; set; }

    /// <summary>JSON snapshot of the entity state after the change (Requirement 10.3).</summary>
    public string? NewValues { get; set; }

    /// <summary>UTC timestamp of the event (Requirement 10.1).</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Optional device that originated the event.</summary>
    public Guid? DeviceId { get; set; }

    /// <summary>Optional IP address of the originating client.</summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    // Soft delete
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
}
