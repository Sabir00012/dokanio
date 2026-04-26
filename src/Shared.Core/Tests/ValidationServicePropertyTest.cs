using Microsoft.Extensions.DependencyInjection;
using Shared.Core.DependencyInjection;
using Shared.Core.DTOs;
using Shared.Core.Enums;
using Shared.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace Shared.Core.Tests;

/// <summary>
/// Property-based tests for ValidationService input validation completeness.
///
/// Feature: sales-service-implementation, Property 16: Input Validation Completeness
/// Validates: Requirements 8.2
///
/// Property: For any input data, the system should validate all inputs before processing
/// and provide specific validation messages for invalid data.
/// </summary>
public class ValidationServicePropertyTest : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IValidationService _validationService;
    private readonly ITestOutputHelper _output;

    // Minimum iterations required by the spec
    private const int MinIterations = 100;

    public ValidationServicePropertyTest(ITestOutputHelper output)
    {
        _output = output;

        var services = new ServiceCollection();
        services.AddSharedCoreInMemory();
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _validationService = _serviceProvider.GetRequiredService<IValidationService>();
    }

    /// <summary>
    /// **Validates: Requirements 8.2**
    ///
    /// Property 16: Input Validation Completeness
    /// For any sale creation input, the system validates all required fields before processing
    /// and returns specific, non-empty error messages for every invalid field.
    /// </summary>
    [Fact]
    public async Task Property16_SaleCreationValidation_InvalidInputsAlwaysProduceSpecificMessages()
    {
        var random = new Random(42);
        int passCount = 0;

        for (int i = 0; i < MinIterations; i++)
        {
            // Generate a random combination of valid/invalid inputs
            var (invoiceNumber, deviceId, userId, customerId) = GenerateRandomSaleCreationInput(random);

            var result = await _validationService.ValidateSaleCreationAsync(
                invoiceNumber, deviceId, userId, customerId);

            // Property: if invalid, every error must have a non-empty message and a known field
            if (!result.IsValid)
            {
                foreach (var error in result.Errors)
                {
                    Assert.False(string.IsNullOrWhiteSpace(error.Message),
                        $"Iteration {i}: Error for field '{error.Field}' must have a non-empty message.");
                    Assert.False(string.IsNullOrWhiteSpace(error.Field),
                        $"Iteration {i}: Every error must identify the field that failed.");
                    Assert.True(Enum.IsDefined(typeof(SaleValidationErrorType), error.Type),
                        $"Iteration {i}: Error type '{error.Type}' must be a defined SaleValidationErrorType.");
                }
            }

            // Property: empty invoice number must always be invalid
            if (string.IsNullOrWhiteSpace(invoiceNumber))
            {
                Assert.False(result.IsValid,
                    $"Iteration {i}: Empty invoice number must always fail validation.");
                Assert.Contains(result.Errors, e => e.Field == "invoiceNumber");
            }

            // Property: empty Guid device ID must always be invalid
            if (deviceId == Guid.Empty)
            {
                Assert.False(result.IsValid,
                    $"Iteration {i}: Empty device ID must always fail validation.");
                Assert.Contains(result.Errors, e => e.Field == "deviceId");
            }

            // Property: empty Guid user ID must always be invalid
            if (userId == Guid.Empty)
            {
                Assert.False(result.IsValid,
                    $"Iteration {i}: Empty user ID must always fail validation.");
                Assert.Contains(result.Errors, e => e.Field == "userId");
            }

            // Property: valid inputs must always pass
            if (!string.IsNullOrWhiteSpace(invoiceNumber) &&
                invoiceNumber.Length <= 50 &&
                deviceId != Guid.Empty &&
                userId != Guid.Empty &&
                (customerId == null || customerId != Guid.Empty))
            {
                Assert.True(result.IsValid,
                    $"Iteration {i}: All valid inputs must pass validation. Errors: {string.Join("; ", result.Errors.Select(e => e.Message))}");
                passCount++;
            }
        }

        _output.WriteLine($"Property 16 (sale creation): {MinIterations} iterations completed. {passCount} fully-valid input sets passed.");
    }

    /// <summary>
    /// **Validates: Requirements 8.2**
    ///
    /// Property 16: Input Validation Completeness
    /// For any product addition input, the system validates quantity > 0 before processing
    /// and returns specific error messages for invalid quantities.
    /// </summary>
    [Fact]
    public async Task Property16_ProductAdditionValidation_InvalidQuantityAlwaysProducesSpecificMessage()
    {
        var random = new Random(123);

        for (int i = 0; i < MinIterations; i++)
        {
            var saleId = random.NextDouble() < 0.1 ? Guid.Empty : Guid.NewGuid();
            var productId = random.NextDouble() < 0.1 ? Guid.Empty : Guid.NewGuid();
            var quantity = GenerateRandomQuantity(random);

            var result = await _validationService.ValidateProductAdditionAsync(saleId, productId, quantity);

            // Property: every error must have a non-empty, specific message
            foreach (var error in result.Errors)
            {
                Assert.False(string.IsNullOrWhiteSpace(error.Message),
                    $"Iteration {i}: Error for field '{error.Field}' must have a non-empty message.");
                Assert.False(string.IsNullOrWhiteSpace(error.Field),
                    $"Iteration {i}: Every error must identify the field that failed.");
            }

            // Property: quantity <= 0 must always be invalid
            if (quantity <= 0)
            {
                Assert.False(result.IsValid,
                    $"Iteration {i}: Quantity {quantity} must always fail validation.");
                Assert.Contains(result.Errors, e => e.Field == "quantity" && e.Type == SaleValidationErrorType.OutOfRange);
            }

            // Property: empty sale ID must always be invalid
            if (saleId == Guid.Empty)
            {
                Assert.False(result.IsValid,
                    $"Iteration {i}: Empty sale ID must always fail validation.");
            }

            // Property: valid inputs must always pass
            if (saleId != Guid.Empty && productId != Guid.Empty && quantity > 0 && quantity <= 10000)
            {
                Assert.True(result.IsValid,
                    $"Iteration {i}: Valid inputs (saleId={saleId}, productId={productId}, qty={quantity}) must pass. Errors: {string.Join("; ", result.Errors.Select(e => e.Message))}");
            }
        }

        _output.WriteLine($"Property 16 (product addition): {MinIterations} iterations completed.");
    }

    /// <summary>
    /// **Validates: Requirements 8.2**
    ///
    /// Property 16: Input Validation Completeness
    /// For any weight-based product addition, the system validates weight > 0 before processing
    /// and returns specific error messages for invalid weights.
    /// </summary>
    [Fact]
    public async Task Property16_WeightBasedProductValidation_InvalidWeightAlwaysProducesSpecificMessage()
    {
        var random = new Random(456);

        for (int i = 0; i < MinIterations; i++)
        {
            var saleId = random.NextDouble() < 0.1 ? Guid.Empty : Guid.NewGuid();
            var productId = random.NextDouble() < 0.1 ? Guid.Empty : Guid.NewGuid();
            var weight = GenerateRandomWeight(random);

            var result = await _validationService.ValidateWeightBasedProductAdditionAsync(saleId, productId, weight);

            // Property: every error must have a non-empty, specific message
            foreach (var error in result.Errors)
            {
                Assert.False(string.IsNullOrWhiteSpace(error.Message),
                    $"Iteration {i}: Error for field '{error.Field}' must have a non-empty message.");
                Assert.False(string.IsNullOrWhiteSpace(error.Field),
                    $"Iteration {i}: Every error must identify the field that failed.");
            }

            // Property: weight <= 0 must always be invalid
            if (weight <= 0)
            {
                Assert.False(result.IsValid,
                    $"Iteration {i}: Weight {weight} must always fail validation.");
                Assert.Contains(result.Errors, e => e.Field == "weight" && e.Type == SaleValidationErrorType.OutOfRange);
            }

            // Property: valid inputs must always pass
            if (saleId != Guid.Empty && productId != Guid.Empty && weight > 0 && weight <= 1000)
            {
                Assert.True(result.IsValid,
                    $"Iteration {i}: Valid inputs (weight={weight}) must pass. Errors: {string.Join("; ", result.Errors.Select(e => e.Message))}");
            }
        }

        _output.WriteLine($"Property 16 (weight-based product): {MinIterations} iterations completed.");
    }

    /// <summary>
    /// **Validates: Requirements 8.2**
    ///
    /// Property 16: Input Validation Completeness
    /// For any sale completion input, the system validates payment method and amount before processing
    /// and returns specific error messages for invalid data.
    /// </summary>
    [Fact]
    public async Task Property16_SaleCompletionValidation_InvalidPaymentAlwaysProducesSpecificMessage()
    {
        var random = new Random(789);
        var validPaymentMethods = Enum.GetValues<PaymentMethod>();

        for (int i = 0; i < MinIterations; i++)
        {
            var saleId = random.NextDouble() < 0.1 ? Guid.Empty : Guid.NewGuid();
            var paymentMethod = validPaymentMethods[random.Next(validPaymentMethods.Length)];
            var amountPaid = GenerateRandomAmount(random);

            var result = await _validationService.ValidateSaleCompletionAsync(saleId, paymentMethod, amountPaid);

            // Property: every error must have a non-empty, specific message
            foreach (var error in result.Errors)
            {
                Assert.False(string.IsNullOrWhiteSpace(error.Message),
                    $"Iteration {i}: Error for field '{error.Field}' must have a non-empty message.");
                Assert.False(string.IsNullOrWhiteSpace(error.Field),
                    $"Iteration {i}: Every error must identify the field that failed.");
            }

            // Property: negative amount must always be invalid
            if (amountPaid < 0)
            {
                Assert.False(result.IsValid,
                    $"Iteration {i}: Negative amount {amountPaid} must always fail validation.");
                Assert.Contains(result.Errors, e => e.Field == "amountPaid" && e.Type == SaleValidationErrorType.OutOfRange);
            }

            // Property: empty sale ID must always be invalid
            if (saleId == Guid.Empty)
            {
                Assert.False(result.IsValid,
                    $"Iteration {i}: Empty sale ID must always fail validation.");
            }

            // Property: valid inputs must always pass
            if (saleId != Guid.Empty && amountPaid > 0)
            {
                Assert.True(result.IsValid,
                    $"Iteration {i}: Valid inputs (saleId={saleId}, method={paymentMethod}, amount={amountPaid}) must pass. Errors: {string.Join("; ", result.Errors.Select(e => e.Message))}");
            }
        }

        _output.WriteLine($"Property 16 (sale completion): {MinIterations} iterations completed.");
    }

    /// <summary>
    /// **Validates: Requirements 8.2**
    ///
    /// Property 16: Input Validation Completeness
    /// Aggregation of validation results must always produce a combined result that is invalid
    /// if any individual result is invalid, and valid only when all results are valid.
    /// </summary>
    [Fact]
    public async Task Property16_ValidationAggregation_AggregatedResultReflectsAllErrors()
    {
        var random = new Random(321);

        for (int i = 0; i < MinIterations; i++)
        {
            // Generate 2-5 random validation results
            var count = random.Next(2, 6);
            var results = new List<SaleValidationResult>();
            var hasAnyInvalid = false;
            var totalExpectedErrors = 0;

            for (int j = 0; j < count; j++)
            {
                var isValid = random.NextDouble() > 0.4; // 60% chance of invalid
                if (!isValid) hasAnyInvalid = true;

                var errors = isValid
                    ? new List<SaleValidationError>()
                    : new List<SaleValidationError>
                    {
                        new()
                        {
                            Field = $"field_{j}",
                            Message = $"Error in result {j}",
                            Type = SaleValidationErrorType.BusinessRule
                        }
                    };

                totalExpectedErrors += errors.Count;
                results.Add(new SaleValidationResult
                {
                    IsValid = isValid,
                    Errors = errors
                });
            }

            var aggregated = _validationService.AggregateValidationResults(results);

            // Property: aggregated result is invalid if any individual result is invalid
            if (hasAnyInvalid)
            {
                Assert.False(aggregated.IsValid,
                    $"Iteration {i}: Aggregated IsValid should be false when any result is invalid.");
            }
            else
            {
                Assert.True(aggregated.IsValid,
                    $"Iteration {i}: Aggregated IsValid should be true when all results are valid.");
            }

            // Property: aggregated error count equals sum of all individual error counts
            var actualErrorCount = aggregated.Errors.Count();
            Assert.True(actualErrorCount == totalExpectedErrors,
                $"Iteration {i}: Aggregated error count should be {totalExpectedErrors} but was {actualErrorCount}.");

            // Property: every aggregated error must have a non-empty message
            foreach (var error in aggregated.Errors)
            {
                Assert.False(string.IsNullOrWhiteSpace(error.Message),
                    $"Iteration {i}: Aggregated error must have a non-empty message.");
            }
        }

        _output.WriteLine($"Property 16 (aggregation): {MinIterations} iterations completed.");
    }

    // ─── Input Generators ────────────────────────────────────────────────────────

    private static (string invoiceNumber, Guid deviceId, Guid userId, Guid? customerId)
        GenerateRandomSaleCreationInput(Random random)
    {
        // Invoice number: 20% chance of empty, 10% chance of too long, otherwise valid
        string invoiceNumber;
        var invoiceCase = random.NextDouble();
        if (invoiceCase < 0.20)
            invoiceNumber = "";
        else if (invoiceCase < 0.30)
            invoiceNumber = new string('X', 51); // too long
        else
            invoiceNumber = $"INV-{random.Next(1000, 9999)}-{random.Next(100, 999)}";

        // Device ID: 15% chance of empty
        var deviceId = random.NextDouble() < 0.15 ? Guid.Empty : Guid.NewGuid();

        // User ID: 15% chance of empty
        var userId = random.NextDouble() < 0.15 ? Guid.Empty : Guid.NewGuid();

        // Customer ID: 40% chance of null, 10% chance of empty Guid, otherwise valid
        Guid? customerId;
        var customerCase = random.NextDouble();
        if (customerCase < 0.40)
            customerId = null;
        else if (customerCase < 0.50)
            customerId = Guid.Empty;
        else
            customerId = Guid.NewGuid();

        return (invoiceNumber, deviceId, userId, customerId);
    }

    private static int GenerateRandomQuantity(Random random)
    {
        // 25% chance of invalid (<=0), 5% chance of too large, rest valid
        var quantityCase = random.NextDouble();
        if (quantityCase < 0.15)
            return 0;
        if (quantityCase < 0.25)
            return random.Next(-100, 0);
        if (quantityCase < 0.30)
            return random.Next(10001, 20000); // too large
        return random.Next(1, 100);
    }

    private static decimal GenerateRandomWeight(Random random)
    {
        // 20% chance of invalid (<=0), 5% chance of too large, rest valid
        var weightCase = random.NextDouble();
        if (weightCase < 0.10)
            return 0m;
        if (weightCase < 0.20)
            return (decimal)(random.NextDouble() * -10); // negative
        if (weightCase < 0.25)
            return (decimal)(random.NextDouble() * 500 + 1001); // too large
        return (decimal)(random.NextDouble() * 99.9 + 0.1); // 0.1 to 100
    }

    private static decimal GenerateRandomAmount(Random random)
    {
        // 20% chance of negative, rest valid
        var amountCase = random.NextDouble();
        if (amountCase < 0.10)
            return 0m;
        if (amountCase < 0.20)
            return (decimal)(random.NextDouble() * -1000); // negative
        return (decimal)(random.NextDouble() * 10000 + 0.01); // 0.01 to 10000
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}
