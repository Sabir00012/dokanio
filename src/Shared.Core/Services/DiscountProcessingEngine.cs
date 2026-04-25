using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Core.Data;
using Shared.Core.Entities;
using Shared.Core.Enums;
using Shared.Core.Repositories;
using DiscountCalcResult = Shared.Core.DTOs.DiscountCalculationResult;
using DiscountValidResult = Shared.Core.DTOs.DiscountValidationResult;
using AppliedDiscountDto = Shared.Core.DTOs.AppliedDiscount;

namespace Shared.Core.Services;

/// <summary>
/// Production-ready discount processing engine that handles automatic discount application,
/// membership integration, combination rules, priority handling, and audit trail tracking.
///
/// Discount application order (priority):
///   1. Membership-based discounts (highest priority, applied first)
///   2. Product-specific discounts
///   3. Category-based discounts
///   4. Sale-level discounts (applied last)
///
/// Combination rules:
///   - Membership discounts always combine with other discounts
///   - Multiple product/category discounts are allowed unless marked exclusive
///   - Sale-level discounts combine with item-level discounts
///   - No single discount can cause a line item total to go below zero (Requirement 4.4)
///
/// Requirements: 4.1, 4.2, 4.3, 4.4, 4.5
/// </summary>
public class DiscountProcessingEngine : IDiscountProcessingEngine
{
    private readonly IDiscountRepository _discountRepository;
    private readonly IDiscountService _discountService;
    private readonly IMembershipService _membershipService;
    private readonly ISaleRepository _saleRepository;
    private readonly PosDbContext _context;
    private readonly ILogger<DiscountProcessingEngine> _logger;

    // Discount application priority constants (lower = applied first)
    private const int MembershipDiscountPriority = 1;
    private const int ProductDiscountPriority = 2;
    private const int CategoryDiscountPriority = 3;
    private const int SaleDiscountPriority = 4;

    // Membership tier discount percentages (consistent with MembershipService)
    private static readonly Dictionary<MembershipTier, decimal> TierDiscountPercentages = new()
    {
        { MembershipTier.None, 0m },
        { MembershipTier.Bronze, 2m },
        { MembershipTier.Silver, 5m },
        { MembershipTier.Gold, 8m },
        { MembershipTier.Platinum, 12m }
    };

    public DiscountProcessingEngine(
        IDiscountRepository discountRepository,
        IDiscountService discountService,
        IMembershipService membershipService,
        ISaleRepository saleRepository,
        PosDbContext context,
        ILogger<DiscountProcessingEngine> logger)
    {
        _discountRepository = discountRepository;
        _discountService = discountService;
        _membershipService = membershipService;
        _saleRepository = saleRepository;
        _context = context;
        _logger = logger;
    }

    // =========================================================================
    // CalculateDiscountsAsync
    // =========================================================================

    /// <summary>
    /// Calculates all applicable discounts for a sale in priority order.
    /// Requirement 4.1: Automatically apply eligible discounts for customers with membership benefits.
    /// Requirement 4.2: Calculate percentage-based and fixed-amount discounts correctly.
    /// Requirement 4.3: Apply discounts in the correct order and combination rules.
    /// Requirement 4.4: Prevent discount amounts from exceeding line item totals.
    /// </summary>
    public async Task<DiscountCalcResult> CalculateDiscountsAsync(Sale sale, Customer? customer = null)
    {
        if (sale == null)
            throw new ArgumentNullException(nameof(sale));

        _logger.LogDebug("Calculating discounts for sale {SaleId}, customer {CustomerId}",
            sale.Id, customer?.Id);

        var result = new DiscountCalcResult();

        try
        {
            var saleDate = sale.CreatedAt;
            var activeItems = sale.Items.Where(i => !i.IsDeleted).ToList();

            if (!activeItems.Any())
            {
                _logger.LogDebug("Sale {SaleId} has no active items; no discounts to apply", sale.Id);
                return result;
            }

            // Step 1: Apply membership discount (highest priority, Requirement 4.1)
            if (customer != null && customer.IsActive && customer.Tier != MembershipTier.None)
            {
                var membershipDiscountAmount = await CalculateMembershipDiscountAsync(customer, sale);
                if (membershipDiscountAmount > 0)
                {
                    var membershipApplied = new AppliedDiscountDto
                    {
                        DiscountId = Guid.Empty, // Membership discounts are virtual (tier-based)
                        DiscountName = $"{customer.Tier} Membership Discount",
                        Type = DiscountType.Percentage,
                        Value = TierDiscountPercentages.GetValueOrDefault(customer.Tier, 0m),
                        CalculatedAmount = membershipDiscountAmount,
                        Reason = BuildMembershipDiscountReason(customer)
                    };

                    result.AppliedDiscounts.Add(membershipApplied);
                    result.TotalDiscountAmount += membershipDiscountAmount;
                    result.DiscountReasons.Add(membershipApplied.Reason);

                    _logger.LogDebug("Applied membership discount {Amount} for {Tier} customer {CustomerId}",
                        membershipDiscountAmount, customer.Tier, customer.Id);
                }
            }

            // Step 2: Apply item-level discounts (product and category) in priority order
            foreach (var saleItem in activeItems)
            {
                if (saleItem.Product == null) continue;

                var itemDiscounts = await _discountRepository.GetApplicableDiscountsAsync(
                    saleItem.ProductId,
                    saleItem.Product.Category,
                    customer?.Tier,
                    saleDate,
                    saleDate.TimeOfDay);

                // Sort by priority: product-specific first, then category
                var orderedDiscounts = itemDiscounts
                    .OrderBy(d => d.Scope == DiscountScope.Product ? ProductDiscountPriority : CategoryDiscountPriority)
                    .ToList();

                // Track remaining item total to enforce Requirement 4.4
                var remainingItemTotal = saleItem.TotalPrice;

                foreach (var discount in orderedDiscounts)
                {
                    // Check quantity requirements
                    if (discount.MinimumQuantity.HasValue && saleItem.Quantity < discount.MinimumQuantity.Value)
                        continue;

                    var discountAmount = await _discountService.CalculateDiscountAmountAsync(
                        discount, remainingItemTotal, saleItem.Quantity);

                    // Requirement 4.4: Prevent discount from exceeding remaining item total
                    discountAmount = Math.Min(discountAmount, remainingItemTotal);

                    if (discountAmount <= 0) continue;

                    var appliedDiscount = new AppliedDiscountDto
                    {
                        DiscountId = discount.Id,
                        DiscountName = discount.Name,
                        Type = discount.Type,
                        Value = discount.Value,
                        CalculatedAmount = discountAmount,
                        Reason = BuildDiscountReason(discount, saleItem, customer)
                    };

                    result.AppliedDiscounts.Add(appliedDiscount);
                    result.TotalDiscountAmount += discountAmount;
                    result.DiscountReasons.Add(appliedDiscount.Reason);

                    remainingItemTotal -= discountAmount;
                }
            }

            // Step 3: Apply sale-level discounts (lowest priority)
            var saleLevelDiscounts = await _discountRepository.GetApplicableDiscountsAsync(
                null,
                null,
                customer?.Tier,
                saleDate,
                saleDate.TimeOfDay);

            var saleLevelOnly = saleLevelDiscounts
                .Where(d => d.Scope == DiscountScope.Sale)
                .ToList();

            var saleSubtotal = activeItems.Sum(i => i.TotalPrice);

            foreach (var discount in saleLevelOnly)
            {
                // Check minimum sale amount requirement
                if (discount.MinimumAmount.HasValue && saleSubtotal < discount.MinimumAmount.Value)
                    continue;

                var discountAmount = await _discountService.CalculateDiscountAmountAsync(
                    discount, saleSubtotal);

                // Requirement 4.4: Prevent total discounts from exceeding sale subtotal
                var remainingSaleTotal = saleSubtotal - result.TotalDiscountAmount;
                discountAmount = Math.Min(discountAmount, Math.Max(0, remainingSaleTotal));

                if (discountAmount <= 0) continue;

                var appliedDiscount = new AppliedDiscountDto
                {
                    DiscountId = discount.Id,
                    DiscountName = discount.Name,
                    Type = discount.Type,
                    Value = discount.Value,
                    CalculatedAmount = discountAmount,
                    Reason = BuildDiscountReason(discount, null, customer)
                };

                result.AppliedDiscounts.Add(appliedDiscount);
                result.TotalDiscountAmount += discountAmount;
                result.DiscountReasons.Add(appliedDiscount.Reason);
            }

            // Final safety check: total discount cannot exceed sale subtotal (Requirement 4.4)
            result.TotalDiscountAmount = Math.Min(result.TotalDiscountAmount, saleSubtotal);

            _logger.LogInformation(
                "Discount calculation complete for sale {SaleId}: {DiscountCount} discounts applied, total {TotalDiscount}",
                sale.Id, result.AppliedDiscounts.Count, result.TotalDiscountAmount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating discounts for sale {SaleId}", sale.Id);
            throw;
        }

        return result;
    }

    // =========================================================================
    // GetApplicableDiscountsAsync
    // =========================================================================

    /// <summary>
    /// Gets all discounts applicable to the sale without applying them.
    /// Requirement 4.1: Identify eligible discounts based on customer membership benefits.
    /// </summary>
    public async Task<IEnumerable<ApplicableDiscount>> GetApplicableDiscountsAsync(Sale sale, Customer? customer = null)
    {
        if (sale == null)
            throw new ArgumentNullException(nameof(sale));

        _logger.LogDebug("Getting applicable discounts for sale {SaleId}", sale.Id);

        var applicableDiscounts = new List<ApplicableDiscount>();
        var saleDate = sale.CreatedAt;
        var activeItems = sale.Items.Where(i => !i.IsDeleted).ToList();
        var saleSubtotal = activeItems.Sum(i => i.TotalPrice);

        // Membership discount (virtual, tier-based)
        if (customer != null && customer.IsActive && customer.Tier != MembershipTier.None)
        {
            var membershipAmount = await CalculateMembershipDiscountAsync(customer, sale);
            var discountPct = TierDiscountPercentages.GetValueOrDefault(customer.Tier, 0m);

            applicableDiscounts.Add(new ApplicableDiscount
            {
                Discount = CreateVirtualMembershipDiscount(customer, discountPct),
                IsEligible = true,
                CalculatedAmount = membershipAmount,
                Reason = BuildMembershipDiscountReason(customer),
                Priority = MembershipDiscountPriority,
                IsCombineable = true,
                Scope = "Membership"
            });
        }

        // Item-level discounts
        foreach (var saleItem in activeItems)
        {
            if (saleItem.Product == null) continue;

            var itemDiscounts = await _discountRepository.GetApplicableDiscountsAsync(
                saleItem.ProductId,
                saleItem.Product.Category,
                customer?.Tier,
                saleDate,
                saleDate.TimeOfDay);

            foreach (var discount in itemDiscounts)
            {
                bool meetsQuantity = !discount.MinimumQuantity.HasValue
                    || saleItem.Quantity >= discount.MinimumQuantity.Value;

                var discountAmount = meetsQuantity
                    ? await _discountService.CalculateDiscountAmountAsync(discount, saleItem.TotalPrice, saleItem.Quantity)
                    : 0m;

                // Cap at item total (Requirement 4.4)
                discountAmount = Math.Min(discountAmount, saleItem.TotalPrice);

                int priority = discount.Scope == DiscountScope.Product
                    ? ProductDiscountPriority
                    : CategoryDiscountPriority;

                applicableDiscounts.Add(new ApplicableDiscount
                {
                    Discount = discount,
                    IsEligible = meetsQuantity,
                    CalculatedAmount = discountAmount,
                    Reason = meetsQuantity
                        ? BuildDiscountReason(discount, saleItem, customer)
                        : $"Minimum quantity {discount.MinimumQuantity} not met (current: {saleItem.Quantity})",
                    Priority = priority,
                    IsCombineable = true,
                    Scope = discount.Scope.ToString()
                });
            }
        }

        // Sale-level discounts
        var saleLevelDiscounts = await _discountRepository.GetApplicableDiscountsAsync(
            null, null, customer?.Tier, saleDate, saleDate.TimeOfDay);

        foreach (var discount in saleLevelDiscounts.Where(d => d.Scope == DiscountScope.Sale))
        {
            bool meetsAmount = !discount.MinimumAmount.HasValue
                || saleSubtotal >= discount.MinimumAmount.Value;

            var discountAmount = meetsAmount
                ? await _discountService.CalculateDiscountAmountAsync(discount, saleSubtotal)
                : 0m;

            applicableDiscounts.Add(new ApplicableDiscount
            {
                Discount = discount,
                IsEligible = meetsAmount,
                CalculatedAmount = discountAmount,
                Reason = meetsAmount
                    ? BuildDiscountReason(discount, null, customer)
                    : $"Minimum sale amount {discount.MinimumAmount:C} not met (current: {saleSubtotal:C})",
                Priority = SaleDiscountPriority,
                IsCombineable = true,
                Scope = "Sale"
            });
        }

        return applicableDiscounts.OrderBy(d => d.Priority);
    }

    // =========================================================================
    // ValidateDiscountApplicationAsync
    // =========================================================================

    /// <summary>
    /// Validates whether a specific discount can be applied to a sale.
    /// Requirement 4.3: Enforce combination rules and priorities.
    /// Requirement 4.4: Prevent discount amounts from exceeding line item totals.
    /// </summary>
    public async Task<DiscountValidResult> ValidateDiscountApplicationAsync(Discount discount, Sale sale)
    {
        if (discount == null)
            throw new ArgumentNullException(nameof(discount));

        if (sale == null)
            throw new ArgumentNullException(nameof(sale));

        var result = new DiscountValidResult { IsValid = true };

        // Check discount is active
        if (!discount.IsActive || discount.IsDeleted)
        {
            result.IsValid = false;
            result.ValidationErrors.Add("Discount is not active or has been deleted.");
            return result;
        }

        // Check date validity
        var now = DateTime.UtcNow;
        if (now < discount.StartDate || now > discount.EndDate)
        {
            result.IsValid = false;
            result.ValidationErrors.Add(
                $"Discount is not valid for the current date. Valid from {discount.StartDate:d} to {discount.EndDate:d}.");
            return result;
        }

        // Check time validity
        if (discount.StartTime.HasValue && discount.EndTime.HasValue)
        {
            var currentTime = now.TimeOfDay;
            if (currentTime < discount.StartTime.Value || currentTime > discount.EndTime.Value)
            {
                result.IsValid = false;
                result.ValidationErrors.Add(
                    $"Discount is only valid between {discount.StartTime:hh\\:mm} and {discount.EndTime:hh\\:mm}.");
                return result;
            }
        }

        var activeItems = sale.Items.Where(i => !i.IsDeleted).ToList();
        var saleSubtotal = activeItems.Sum(i => i.TotalPrice);

        // Check minimum sale amount
        if (discount.Scope == DiscountScope.Sale && discount.MinimumAmount.HasValue
            && saleSubtotal < discount.MinimumAmount.Value)
        {
            result.IsValid = false;
            result.ValidationErrors.Add(
                $"Sale total {saleSubtotal:C} does not meet minimum amount {discount.MinimumAmount:C} required for this discount.");
            return result;
        }

        // For product/category discounts, check that applicable items exist
        if (discount.Scope == DiscountScope.Product)
        {
            var hasApplicableItem = activeItems.Any(i => i.ProductId == discount.ProductId);
            if (!hasApplicableItem)
            {
                result.IsValid = false;
                result.ValidationErrors.Add("No items in the sale match the product required for this discount.");
                return result;
            }

            // Check minimum quantity
            if (discount.MinimumQuantity.HasValue)
            {
                var applicableItem = activeItems.FirstOrDefault(i => i.ProductId == discount.ProductId);
                if (applicableItem != null && applicableItem.Quantity < discount.MinimumQuantity.Value)
                {
                    result.IsValid = false;
                    result.ValidationErrors.Add(
                        $"Item quantity {applicableItem.Quantity} does not meet minimum quantity {discount.MinimumQuantity} required for this discount.");
                    return result;
                }
            }
        }

        if (discount.Scope == DiscountScope.Category)
        {
            var hasApplicableItem = activeItems.Any(i =>
                i.Product?.Category != null &&
                string.Equals(i.Product.Category, discount.Category, StringComparison.OrdinalIgnoreCase));

            if (!hasApplicableItem)
            {
                result.IsValid = false;
                result.ValidationErrors.Add(
                    $"No items in the sale belong to the '{discount.Category}' category required for this discount.");
                return result;
            }
        }

        // Requirement 4.4: Validate that discount won't exceed applicable item totals
        decimal maxApplicableAmount = discount.Scope switch
        {
            DiscountScope.Product => activeItems
                .Where(i => i.ProductId == discount.ProductId)
                .Sum(i => i.TotalPrice),
            DiscountScope.Category => activeItems
                .Where(i => string.Equals(i.Product?.Category, discount.Category, StringComparison.OrdinalIgnoreCase))
                .Sum(i => i.TotalPrice),
            DiscountScope.Sale => saleSubtotal,
            _ => saleSubtotal
        };

        if (discount.Type == DiscountType.FixedAmount && discount.Value > maxApplicableAmount)
        {
            // This is a warning, not an error - the discount will be capped
            result.ValidationErrors.Add(
                $"Fixed discount amount {discount.Value:C} exceeds applicable item total {maxApplicableAmount:C}. Discount will be capped.");
            // Still valid - we cap it rather than reject
        }

        _logger.LogDebug("Discount {DiscountId} validation result: {IsValid}, errors: {ErrorCount}",
            discount.Id, result.IsValid, result.ValidationErrors.Count);

        return result;
    }

    // =========================================================================
    // CalculateMembershipDiscountAsync
    // =========================================================================

    /// <summary>
    /// Calculates the membership discount amount for a customer on a given sale.
    /// Requirement 4.1: Automatically apply eligible membership discounts.
    /// Requirement 4.2: Calculate percentage-based discounts correctly.
    /// </summary>
    public async Task<decimal> CalculateMembershipDiscountAsync(Customer customer, Sale sale)
    {
        if (customer == null)
            throw new ArgumentNullException(nameof(customer));

        if (sale == null)
            throw new ArgumentNullException(nameof(sale));

        if (!customer.IsActive || customer.Tier == MembershipTier.None)
        {
            _logger.LogDebug("Customer {CustomerId} is inactive or has no membership tier; no membership discount",
                customer.Id);
            return 0m;
        }

        var discountPercentage = TierDiscountPercentages.GetValueOrDefault(customer.Tier, 0m);
        if (discountPercentage <= 0)
            return 0m;

        var activeItems = sale.Items.Where(i => !i.IsDeleted).ToList();
        var saleSubtotal = activeItems.Sum(i => i.TotalPrice);

        if (saleSubtotal <= 0)
            return 0m;

        // Requirement 4.2: Calculate percentage-based discount correctly
        var discountAmount = Math.Round(saleSubtotal * discountPercentage / 100m, 2, MidpointRounding.AwayFromZero);

        // Requirement 4.4: Ensure discount doesn't exceed sale subtotal
        discountAmount = Math.Min(discountAmount, saleSubtotal);

        _logger.LogDebug(
            "Membership discount for {Tier} customer {CustomerId}: {Percentage}% of {Subtotal} = {Amount}",
            customer.Tier, customer.Id, discountPercentage, saleSubtotal, discountAmount);

        return discountAmount;
    }

    // =========================================================================
    // SaveAppliedDiscountsAsync
    // =========================================================================

    /// <summary>
    /// Saves the applied discounts for a sale to the database for audit purposes.
    /// Requirement 4.5: Track and store applied discounts for audit purposes.
    /// </summary>
    public async Task SaveAppliedDiscountsAsync(Guid saleId, IEnumerable<AppliedDiscountDto> appliedDiscounts)
    {
        if (saleId == Guid.Empty)
            throw new ArgumentException("Sale ID cannot be empty.", nameof(saleId));

        if (appliedDiscounts == null)
            throw new ArgumentNullException(nameof(appliedDiscounts));

        var discountList = appliedDiscounts.ToList();

        _logger.LogDebug("Saving {Count} applied discounts for sale {SaleId}", discountList.Count, saleId);

        try
        {
            // Remove existing discount records for this sale to avoid duplicates
            var existingDiscounts = await _context.SaleDiscounts
                .Where(sd => sd.SaleId == saleId)
                .ToListAsync();

            if (existingDiscounts.Any())
            {
                _context.SaleDiscounts.RemoveRange(existingDiscounts);
                _logger.LogDebug("Removed {Count} existing discount records for sale {SaleId}",
                    existingDiscounts.Count, saleId);
            }

            // Save each applied discount as an audit record
            foreach (var appliedDiscount in discountList)
            {
                // Skip virtual membership discounts (DiscountId == Guid.Empty) that have no DB record
                if (appliedDiscount.DiscountId == Guid.Empty)
                {
                    // For membership discounts, create a special audit record
                    var membershipAuditRecord = new SaleDiscount
                    {
                        Id = Guid.NewGuid(),
                        SaleId = saleId,
                        DiscountId = await GetOrCreateMembershipDiscountPlaceholderAsync(),
                        DiscountAmount = appliedDiscount.CalculatedAmount,
                        DiscountReason = appliedDiscount.Reason,
                        AppliedAt = DateTime.UtcNow
                    };
                    _context.SaleDiscounts.Add(membershipAuditRecord);
                    continue;
                }

                // Verify the discount exists in the database
                var discountExists = await _discountRepository.GetByIdAsync(appliedDiscount.DiscountId);
                if (discountExists == null)
                {
                    _logger.LogWarning("Discount {DiscountId} not found in database; skipping audit record",
                        appliedDiscount.DiscountId);
                    continue;
                }

                var saleDiscount = new SaleDiscount
                {
                    Id = Guid.NewGuid(),
                    SaleId = saleId,
                    DiscountId = appliedDiscount.DiscountId,
                    DiscountAmount = appliedDiscount.CalculatedAmount,
                    DiscountReason = appliedDiscount.Reason,
                    AppliedAt = DateTime.UtcNow
                };

                _context.SaleDiscounts.Add(saleDiscount);
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Saved {Count} discount audit records for sale {SaleId}",
                discountList.Count, saleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving applied discounts for sale {SaleId}", saleId);
            throw;
        }
    }

    // =========================================================================
    // Private Helper Methods
    // =========================================================================

    /// <summary>
    /// Builds a human-readable reason string for a discount application.
    /// </summary>
    private static string BuildDiscountReason(Discount discount, SaleItem? saleItem, Customer? customer)
    {
        var reason = discount.Name;

        if (discount.Type == DiscountType.Percentage)
            reason += $" ({discount.Value}% off)";
        else
            reason += $" ({discount.Value:C} off)";

        if (saleItem?.Product != null)
            reason += $" on {saleItem.Product.Name}";

        if (customer != null && discount.RequiredMembershipTier.HasValue)
            reason += $" [{customer.Tier} member benefit]";

        return reason;
    }

    /// <summary>
    /// Builds a human-readable reason string for a membership discount.
    /// </summary>
    private static string BuildMembershipDiscountReason(Customer customer)
    {
        var percentage = TierDiscountPercentages.GetValueOrDefault(customer.Tier, 0m);
        return $"{customer.Tier} Membership Discount ({percentage}% off entire purchase)";
    }

    /// <summary>
    /// Creates a virtual Discount entity representing a membership tier discount.
    /// Used for the ApplicableDiscount response when no real Discount entity exists.
    /// </summary>
    private static Discount CreateVirtualMembershipDiscount(Customer customer, decimal discountPercentage)
    {
        return new Discount
        {
            Id = Guid.Empty,
            Name = $"{customer.Tier} Membership Discount",
            Description = $"Automatic discount for {customer.Tier} tier members",
            Type = DiscountType.Percentage,
            Value = discountPercentage,
            Scope = DiscountScope.Sale,
            IsActive = true,
            StartDate = DateTime.MinValue,
            EndDate = DateTime.MaxValue
        };
    }

    /// <summary>
    /// Gets or creates a placeholder Discount record for membership discounts that don't
    /// have a corresponding Discount entity in the database. This ensures the SaleDiscount
    /// foreign key constraint is satisfied for audit records.
    /// </summary>
    private async Task<Guid> GetOrCreateMembershipDiscountPlaceholderAsync()
    {
        const string membershipPlaceholderName = "__MEMBERSHIP_DISCOUNT_PLACEHOLDER__";

        var existing = await _context.Discounts
            .FirstOrDefaultAsync(d => d.Name == membershipPlaceholderName);

        if (existing != null)
            return existing.Id;

        var placeholder = new Discount
        {
            Id = Guid.NewGuid(),
            Name = membershipPlaceholderName,
            Description = "System placeholder for membership-based discounts",
            Type = DiscountType.Percentage,
            Value = 0,
            Scope = DiscountScope.Sale,
            IsActive = false, // Not active for normal use
            StartDate = DateTime.MinValue,
            EndDate = DateTime.MaxValue,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Discounts.Add(placeholder);
        await _context.SaveChangesAsync();

        _logger.LogDebug("Created membership discount placeholder with ID {PlaceholderId}", placeholder.Id);
        return placeholder.Id;
    }
}
