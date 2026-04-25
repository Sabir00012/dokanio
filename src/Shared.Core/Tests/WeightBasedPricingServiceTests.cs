using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Shared.Core.Entities;
using Shared.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace Shared.Core.Tests;

/// <summary>
/// Unit tests for WeightBasedPricingService.
/// Covers: precision rounding, rate-per-kilogram calculations, weight validation,
/// and product-specific min/max constraints.
/// Requirements: 2.3, 5.1, 5.2, 5.3, 5.5
/// </summary>
public class WeightBasedPricingServiceTests
{
    private readonly IWeightBasedPricingService _service;
    private readonly ITestOutputHelper _output;

    public WeightBasedPricingServiceTests(ITestOutputHelper output)
    {
        _output = output;
        // WeightBasedPricingService has no dependencies - instantiate directly
        _service = new WeightBasedPricingService();
    }

    // =========================================================================
    // Helper: Create a standard weight-based product
    // =========================================================================

    private static Product CreateWeightProduct(
        decimal ratePerKg = 10.00m,
        int precision = 3,
        decimal? minWeightKg = null,
        decimal? maxWeightKg = null)
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            Name = "Test Weight Product",
            IsWeightBased = true,
            RatePerKilogram = ratePerKg,
            WeightPrecision = precision,
            MinWeightKg = minWeightKg,
            MaxWeightKg = maxWeightKg,
            IsActive = true
        };
    }

    // =========================================================================
    // CalculatePriceAsync Tests (Requirement 5.3)
    // =========================================================================

    [Fact]
    public async Task CalculatePriceAsync_BasicCalculation_ShouldReturnWeightTimesRate()
    {
        var product = CreateWeightProduct(ratePerKg: 10.00m, precision: 3);

        var price = await _service.CalculatePriceAsync(product, 2.500m);

        Assert.Equal(25.00m, price);
        _output.WriteLine($"2.500 kg × $10.00/kg = ${price}");
    }

    [Fact]
    public async Task CalculatePriceAsync_FractionalWeight_ShouldRoundCurrencyToTwoDecimals()
    {
        var product = CreateWeightProduct(ratePerKg: 3.00m, precision: 3);

        // 1.333 kg × $3.00/kg = $3.999 → rounds to $4.00
        var price = await _service.CalculatePriceAsync(product, 1.333m);

        Assert.Equal(4.00m, price);
        _output.WriteLine($"1.333 kg × $3.00/kg = ${price}");
    }

    [Fact]
    public async Task CalculatePriceAsync_SmallWeight_ShouldCalculateCorrectly()
    {
        var product = CreateWeightProduct(ratePerKg: 100.00m, precision: 3);

        // 0.001 kg × $100.00/kg = $0.10
        var price = await _service.CalculatePriceAsync(product, 0.001m);

        Assert.Equal(0.10m, price);
        _output.WriteLine($"0.001 kg × $100.00/kg = ${price}");
    }

    [Fact]
    public async Task CalculatePriceAsync_NonWeightBasedProduct_ShouldThrowInvalidOperationException()
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Regular Product",
            IsWeightBased = false,
            UnitPrice = 5.00m
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CalculatePriceAsync(product, 1.0m));
    }

    [Fact]
    public async Task CalculatePriceAsync_ProductWithoutRate_ShouldThrowInvalidOperationException()
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Weight Product No Rate",
            IsWeightBased = true,
            RatePerKilogram = null
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CalculatePriceAsync(product, 1.0m));
    }

    [Fact]
    public async Task CalculatePriceAsync_NullProduct_ShouldThrowArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.CalculatePriceAsync(null!, 1.0m));
    }

    // =========================================================================
    // RoundWeight Tests (Requirement 5.2)
    // =========================================================================

    [Theory]
    [InlineData(1.2345, 2, 1.23)]
    [InlineData(1.2355, 2, 1.24)]  // AwayFromZero rounding
    [InlineData(1.2345, 3, 1.235)]
    [InlineData(1.2345, 0, 1.0)]
    [InlineData(1.5, 0, 2.0)]      // AwayFromZero: 1.5 rounds to 2
    public void RoundWeight_ShouldRoundToSpecifiedPrecision(double inputDouble, int precision, double expectedDouble)
    {
        var input = (decimal)inputDouble;
        var expected = (decimal)expectedDouble;

        var result = _service.RoundWeight(input, precision);

        Assert.Equal(expected, result);
        _output.WriteLine($"RoundWeight({input}, {precision}) = {result} (expected {expected})");
    }

    [Fact]
    public void RoundWeight_NegativePrecision_ShouldUseDefaultPrecision()
    {
        // Negative precision falls back to default (3)
        var result = _service.RoundWeight(1.23456789m, -1);

        Assert.Equal(1.235m, result);
    }

    [Fact]
    public void RoundWeight_PrecisionAboveMax_ShouldUseDefaultPrecision()
    {
        // Precision > 6 falls back to default (3)
        var result = _service.RoundWeight(1.23456789m, 10);

        Assert.Equal(1.235m, result);
    }

    // =========================================================================
    // FormatWeight Tests
    // =========================================================================

    [Theory]
    [InlineData(1.500, 3, "1.500 kg")]
    [InlineData(2.0, 0, "2 kg")]
    [InlineData(0.250, 2, "0.25 kg")]
    public void FormatWeight_ShouldFormatWithCorrectPrecision(double weightDouble, int precision, string expected)
    {
        var weight = (decimal)weightDouble;
        var result = _service.FormatWeight(weight, precision);

        Assert.Equal(expected, result);
        _output.WriteLine($"FormatWeight({weight}, {precision}) = '{result}'");
    }

    // =========================================================================
    // ValidateWeightAsync Tests (Requirements 5.1, 5.5)
    // =========================================================================

    [Fact]
    public async Task ValidateWeightAsync_ValidWeight_ShouldReturnTrue()
    {
        var product = CreateWeightProduct(precision: 3);
        var isValid = await _service.ValidateWeightAsync(1.500m, product);
        Assert.True(isValid);
    }

    [Fact]
    public async Task ValidateWeightAsync_ZeroWeight_ShouldReturnFalse()
    {
        var product = CreateWeightProduct(precision: 3);
        var isValid = await _service.ValidateWeightAsync(0m, product);
        Assert.False(isValid);
    }

    [Fact]
    public async Task ValidateWeightAsync_NegativeWeight_ShouldReturnFalse()
    {
        var product = CreateWeightProduct(precision: 3);
        var isValid = await _service.ValidateWeightAsync(-1.0m, product);
        Assert.False(isValid);
    }

    [Fact]
    public async Task ValidateWeightAsync_WeightBelowGlobalMinimum_ShouldReturnFalse()
    {
        var product = CreateWeightProduct(precision: 3);
        // Global minimum is 0.001 kg; 0.0001 is below it
        var isValid = await _service.ValidateWeightAsync(0.0001m, product);
        Assert.False(isValid);
    }

    [Fact]
    public async Task ValidateWeightAsync_WeightAboveGlobalMaximum_ShouldReturnFalse()
    {
        var product = CreateWeightProduct(precision: 3);
        // Global maximum is 999.999 kg
        var isValid = await _service.ValidateWeightAsync(1000.0m, product);
        Assert.False(isValid);
    }

    [Fact]
    public async Task ValidateWeightAsync_WeightBelowProductMinimum_ShouldReturnFalse()
    {
        // Product requires at least 0.5 kg (Requirement 5.5)
        var product = CreateWeightProduct(precision: 3, minWeightKg: 0.5m);
        var isValid = await _service.ValidateWeightAsync(0.250m, product);
        Assert.False(isValid);
    }

    [Fact]
    public async Task ValidateWeightAsync_WeightAboveProductMaximum_ShouldReturnFalse()
    {
        // Product allows at most 5.0 kg (Requirement 5.5)
        var product = CreateWeightProduct(precision: 3, maxWeightKg: 5.0m);
        var isValid = await _service.ValidateWeightAsync(6.0m, product);
        Assert.False(isValid);
    }

    [Fact]
    public async Task ValidateWeightAsync_WeightAtProductMinimum_ShouldReturnTrue()
    {
        var product = CreateWeightProduct(precision: 3, minWeightKg: 0.5m);
        var isValid = await _service.ValidateWeightAsync(0.500m, product);
        Assert.True(isValid);
    }

    [Fact]
    public async Task ValidateWeightAsync_WeightAtProductMaximum_ShouldReturnTrue()
    {
        var product = CreateWeightProduct(precision: 3, maxWeightKg: 5.0m);
        var isValid = await _service.ValidateWeightAsync(5.000m, product);
        Assert.True(isValid);
    }

    [Fact]
    public async Task ValidateWeightAsync_WeightWithExcessivePrecision_ShouldReturnFalse()
    {
        // Product has precision 2 (e.g., 0.01 kg steps)
        var product = CreateWeightProduct(precision: 2);
        // 1.234 has 3 decimal places, exceeds precision of 2
        var isValid = await _service.ValidateWeightAsync(1.234m, product);
        Assert.False(isValid);
    }

    [Fact]
    public async Task ValidateWeightAsync_WeightWithinPrecision_ShouldReturnTrue()
    {
        var product = CreateWeightProduct(precision: 2);
        var isValid = await _service.ValidateWeightAsync(1.23m, product);
        Assert.True(isValid);
    }

    // =========================================================================
    // GetPricingDetailsAsync Tests (Requirement 5.3)
    // =========================================================================

    [Fact]
    public async Task GetPricingDetailsAsync_ValidInput_ShouldReturnCompleteResult()
    {
        var product = CreateWeightProduct(ratePerKg: 5.00m, precision: 3);

        var result = await _service.GetPricingDetailsAsync(product, 2.500m);

        Assert.True(result.IsValid);
        Assert.Equal(2.500m, result.Weight);
        Assert.Equal(5.00m, result.RatePerKilogram);
        Assert.Equal(12.50m, result.TotalPrice);
        Assert.NotEmpty(result.FormattedWeight);
        Assert.NotEmpty(result.FormattedRate);
        Assert.NotEmpty(result.FormattedPrice);
        Assert.Empty(result.ValidationErrors);
    }

    [Fact]
    public async Task GetPricingDetailsAsync_InvalidWeight_ShouldReturnInvalidResult()
    {
        var product = CreateWeightProduct(precision: 3, maxWeightKg: 5.0m);

        var result = await _service.GetPricingDetailsAsync(product, 10.0m);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.ValidationErrors);
    }

    [Fact]
    public async Task GetPricingDetailsAsync_NonWeightBasedProduct_ShouldReturnInvalidResult()
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Regular Product",
            IsWeightBased = false,
            UnitPrice = 5.00m
        };

        var result = await _service.GetPricingDetailsAsync(product, 1.0m);

        Assert.False(result.IsValid);
        Assert.Contains("not weight-based", result.ValidationErrors[0]);
    }

    [Fact]
    public async Task GetPricingDetailsAsync_WeightIsRoundedToProductPrecision()
    {
        // Product has precision 2 (0.01 kg steps)
        var product = CreateWeightProduct(ratePerKg: 10.00m, precision: 2);

        // Input 1.23 is valid at precision 2
        var result = await _service.GetPricingDetailsAsync(product, 1.23m);

        Assert.True(result.IsValid);
        Assert.Equal(1.23m, result.Weight);
        Assert.Equal(12.30m, result.TotalPrice);
    }
}

/// <summary>
/// Property-based tests for WeightBasedPricingService.
/// Separated from unit tests to avoid xUnit context issues with FsCheck's thread pool execution.
/// Requirements: 2.3, 5.1, 5.2, 5.3, 5.5
/// </summary>
public class WeightBasedPricingPropertyTests
{
    private static readonly IWeightBasedPricingService Service = new WeightBasedPricingService();

    private static Product CreateWeightProduct(
        decimal ratePerKg = 10.00m,
        int precision = 3,
        decimal? minWeightKg = null,
        decimal? maxWeightKg = null)
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            Name = "Test Weight Product",
            IsWeightBased = true,
            RatePerKilogram = ratePerKg,
            WeightPrecision = precision,
            MinWeightKg = minWeightKg,
            MaxWeightKg = maxWeightKg,
            IsActive = true
        };
    }

    /// <summary>
    /// Property 6: Weight-Based Pricing Calculation
    /// For any weight-based product and valid weight value, the system should calculate
    /// pricing using rate-per-kilogram and round according to product precision settings.
    /// Validates: Requirements 2.3, 5.2, 5.3
    /// Feature: sales-service-implementation, Property 6: Weight-Based Pricing Calculation
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(WeightPricingArbitraries) })]
    public bool Property6_WeightBasedPricingCalculation_PriceEqualsWeightTimesRate(
        ValidWeightPricingInput input)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Test Product",
            IsWeightBased = true,
            RatePerKilogram = input.RatePerKg,
            WeightPrecision = input.Precision,
            IsActive = true
        };

        // Act: price = rounded_weight × rate, rounded to 2 decimal places (Requirement 5.3)
        var roundedWeight = Service.RoundWeight(input.Weight, input.Precision);
        var expectedPrice = Math.Round(roundedWeight * input.RatePerKg, 2, MidpointRounding.AwayFromZero);
        var actualPrice = Service.CalculatePriceAsync(product, roundedWeight).GetAwaiter().GetResult();

        return actualPrice == expectedPrice;
    }

    /// <summary>
    /// Property 6b: Rounding precision is applied correctly
    /// For any weight and precision, RoundWeight should produce a value with at most
    /// 'precision' decimal places.
    /// Validates: Requirement 5.2
    /// Feature: sales-service-implementation, Property 6: Weight-Based Pricing Calculation
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(WeightPricingArbitraries) })]
    public bool Property6b_RoundWeight_ShouldRespectPrecision(ValidWeightPricingInput input)
    {
        var rounded = Service.RoundWeight(input.Weight, input.Precision);

        // The rounded value should have at most 'precision' decimal places
        var bits = decimal.GetBits(rounded);
        var scale = (bits[3] >> 16) & 0x7F; // scale is bits 16-23 of the flags word
        return scale <= input.Precision;
    }

    /// <summary>
    /// Property 11: Weight Validation and Limits
    /// For any weight-based product addition, the system should validate weight values
    /// against minimum and maximum limits and require weight input.
    /// Validates: Requirements 5.1, 5.5
    /// Feature: sales-service-implementation, Property 11: Weight Validation and Limits
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(WeightPricingArbitraries) })]
    public bool Property11_WeightValidation_ShouldEnforceProductConstraints(
        WeightConstraintInput input)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Constrained Product",
            IsWeightBased = true,
            RatePerKilogram = 10.00m,
            WeightPrecision = 3,
            MinWeightKg = input.MinWeight,
            MaxWeightKg = input.MaxWeight
        };

        var isValid = Service.ValidateWeightAsync(input.TestWeight, product).GetAwaiter().GetResult();

        // Determine expected validity based on all constraints
        bool shouldBeValid = input.TestWeight > 0
            && input.TestWeight >= 0.001m
            && input.TestWeight <= 999.999m
            && (!input.MinWeight.HasValue || input.TestWeight >= input.MinWeight.Value)
            && (!input.MaxWeight.HasValue || input.TestWeight <= input.MaxWeight.Value);

        return isValid == shouldBeValid;
    }
}

// =========================================================================
// Arbitraries for property-based tests
// =========================================================================

/// <summary>
/// Input for weight pricing property tests
/// </summary>
public record ValidWeightPricingInput(decimal Weight, decimal RatePerKg, int Precision);

/// <summary>
/// Input for weight constraint property tests
/// </summary>
public record WeightConstraintInput(decimal TestWeight, decimal? MinWeight, decimal? MaxWeight);

/// <summary>
/// FsCheck arbitraries for weight-based pricing tests
/// </summary>
public static class WeightPricingArbitraries
{
    /// <summary>
    /// Generates valid weight pricing inputs: weight in [0.001, 999.999], rate in [0.01, 9999.99], precision in [0, 6]
    /// </summary>
    public static Arbitrary<ValidWeightPricingInput> ValidWeightPricingInputArbitrary()
    {
        var precisionGen = Gen.Choose(0, 6);

        var gen = precisionGen.SelectMany(precision =>
        {
            // Generate weight that is valid for the given precision
            var factor = (int)Math.Pow(10, precision);
            var minInt = Math.Max(1, (int)(0.001m * factor));
            var maxInt = Math.Min(999999, (int)(999.999m * factor));

            return Gen.Choose(minInt, maxInt).SelectMany(w =>
                Gen.Choose(1, 999999).Select(r =>
                    new ValidWeightPricingInput(
                        Weight: Math.Round((decimal)w / factor, precision, MidpointRounding.AwayFromZero),
                        RatePerKg: Math.Round((decimal)r / 100, 2),
                        Precision: precision)));
        });

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates weight constraint inputs with optional min/max bounds
    /// </summary>
    public static Arbitrary<WeightConstraintInput> WeightConstraintInputArbitrary()
    {
        // Generate test weight in [0.001, 10.000] with 3 decimal places
        var weightGen = Gen.Choose(1, 10000).Select(w => Math.Round((decimal)w / 1000, 3));

        // Generate optional min weight in [0.001, 5.000]
        var minWeightGen = Gen.OneOf(
            Gen.Constant<decimal?>(null),
            Gen.Choose(1, 5000).Select(w => (decimal?)Math.Round((decimal)w / 1000, 3)));

        // Generate optional max weight in [1.000, 10.000]
        var maxWeightGen = Gen.OneOf(
            Gen.Constant<decimal?>(null),
            Gen.Choose(1000, 10000).Select(w => (decimal?)Math.Round((decimal)w / 1000, 3)));

        var gen = weightGen.SelectMany(weight =>
            minWeightGen.SelectMany(min =>
                maxWeightGen.Select(max =>
                {
                    // Ensure min <= max when both are set
                    var adjustedMax = min.HasValue && max.HasValue && max.Value < min.Value ? min : max;
                    return new WeightConstraintInput(weight, min, adjustedMax);
                })));

        return gen.ToArbitrary();
    }
}
