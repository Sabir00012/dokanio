using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Core.DependencyInjection;
using Shared.Core.DTOs;
using Shared.Core.Entities;
using Shared.Core.Enums;
using Shared.Core.Services;
using Xunit;
using Xunit.Abstractions;
namespace Shared.Core.Tests;

/// <summary>
/// Unit tests for ValidationService functionality
/// </summary>
public class ValidationServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IValidationService _validationService;
    private readonly ITestOutputHelper _output;

    public ValidationServiceTests(ITestOutputHelper output)
    {
        _output = output;
        
        var services = new ServiceCollection();
        services.AddSharedCoreInMemory();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information));
        
        _serviceProvider = services.BuildServiceProvider();
        _validationService = _serviceProvider.GetRequiredService<IValidationService>();
    }

    [Fact]
    public async Task ValidateFieldAsync_RequiredField_WithEmptyValue_ShouldReturnError()
    {
        // Arrange
        var rules = new FieldValidationRules
        {
            IsRequired = true
        };

        // Act
        var result = await _validationService.ValidateFieldAsync("TestField", "", rules);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("FIELD_REQUIRED", result.Errors[0].Code);
        _output.WriteLine($"Validation result: {result.Errors[0].Message}");
    }

    [Fact]
    public async Task ValidateFieldAsync_RequiredField_WithValidValue_ShouldReturnValid()
    {
        // Arrange
        var rules = new FieldValidationRules
        {
            IsRequired = true,
            MinLength = 3,
            MaxLength = 50
        };

        // Act
        var result = await _validationService.ValidateFieldAsync("TestField", "Valid Value", rules);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        _output.WriteLine("Validation passed for valid required field");
    }

    [Fact]
    public async Task ValidateFieldAsync_MinLength_WithShortValue_ShouldReturnError()
    {
        // Arrange
        var rules = new FieldValidationRules
        {
            MinLength = 5
        };

        // Act
        var result = await _validationService.ValidateFieldAsync("TestField", "Hi", rules);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("MIN_LENGTH", result.Errors[0].Code);
        _output.WriteLine($"Min length validation error: {result.Errors[0].Message}");
    }

    [Fact]
    public async Task ValidateFieldAsync_MaxLength_WithLongValue_ShouldReturnError()
    {
        // Arrange
        var rules = new FieldValidationRules
        {
            MaxLength = 10
        };

        // Act
        var result = await _validationService.ValidateFieldAsync("TestField", "This is a very long string", rules);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("MAX_LENGTH", result.Errors[0].Code);
        _output.WriteLine($"Max length validation error: {result.Errors[0].Message}");
    }

    [Fact]
    public async Task ValidateFieldAsync_NumericRange_WithValidValue_ShouldReturnValid()
    {
        // Arrange
        var rules = new FieldValidationRules
        {
            MinValue = 10,
            MaxValue = 100
        };

        // Act
        var result = await _validationService.ValidateFieldAsync("PriceField", "50", rules);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        _output.WriteLine("Numeric range validation passed");
    }

    [Fact]
    public async Task ValidateFieldAsync_NumericRange_WithInvalidValue_ShouldReturnError()
    {
        // Arrange
        var rules = new FieldValidationRules
        {
            MinValue = 10,
            MaxValue = 100
        };

        // Act
        var result = await _validationService.ValidateFieldAsync("PriceField", "5", rules);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("MIN_VALUE", result.Errors[0].Code);
        _output.WriteLine($"Numeric range validation error: {result.Errors[0].Message}");
    }

    [Fact]
    public async Task ValidateFieldAsync_RegexPattern_WithValidEmail_ShouldReturnValid()
    {
        // Arrange
        var rules = new FieldValidationRules
        {
            RegexPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$"
        };

        // Act
        var result = await _validationService.ValidateFieldAsync("EmailField", "test@example.com", rules);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        _output.WriteLine("Email regex validation passed");
    }

    [Fact]
    public async Task ValidateFieldAsync_RegexPattern_WithInvalidEmail_ShouldReturnError()
    {
        // Arrange
        var rules = new FieldValidationRules
        {
            RegexPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$"
        };

        // Act
        var result = await _validationService.ValidateFieldAsync("EmailField", "invalid-email", rules);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("INVALID_FORMAT", result.Errors[0].Code);
        _output.WriteLine($"Email regex validation error: {result.Errors[0].Message}");
    }

    [Fact]
    public async Task ValidateFieldAsync_AllowedValues_WithValidValue_ShouldReturnValid()
    {
        // Arrange
        var rules = new FieldValidationRules
        {
            AllowedValues = new List<string> { "Small", "Medium", "Large" }
        };

        // Act
        var result = await _validationService.ValidateFieldAsync("SizeField", "Medium", rules);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        _output.WriteLine("Allowed values validation passed");
    }

    [Fact]
    public async Task ValidateFieldAsync_AllowedValues_WithInvalidValue_ShouldReturnError()
    {
        // Arrange
        var rules = new FieldValidationRules
        {
            AllowedValues = new List<string> { "Small", "Medium", "Large" }
        };

        // Act
        var result = await _validationService.ValidateFieldAsync("SizeField", "ExtraLarge", rules);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("INVALID_VALUE", result.Errors[0].Code);
        _output.WriteLine($"Allowed values validation error: {result.Errors[0].Message}");
    }

    [Fact]
    public async Task ValidateFieldAsync_CustomMobileNumberRule_WithValidNumber_ShouldReturnValid()
    {
        // Arrange
        var rules = new FieldValidationRules
        {
            CustomRules = new List<string> { "MOBILE_NUMBER" }
        };

        // Act
        var result = await _validationService.ValidateFieldAsync("PhoneField", "+1234567890", rules);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        _output.WriteLine("Mobile number validation passed");
    }

    [Fact]
    public async Task ValidateFieldAsync_CustomMobileNumberRule_WithInvalidNumber_ShouldReturnError()
    {
        // Arrange
        var rules = new FieldValidationRules
        {
            CustomRules = new List<string> { "MOBILE_NUMBER" }
        };

        // Act
        var result = await _validationService.ValidateFieldAsync("PhoneField", "invalid-phone", rules);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("INVALID_MOBILE_NUMBER", result.Errors[0].Code);
        _output.WriteLine($"Mobile number validation error: {result.Errors[0].Message}");
    }

    [Fact]
    public async Task ValidateFieldAsync_CustomBarcodeRule_WithValidBarcode_ShouldReturnValid()
    {
        // Arrange
        var rules = new FieldValidationRules
        {
            CustomRules = new List<string> { "BARCODE" }
        };

        // Act
        var result = await _validationService.ValidateFieldAsync("BarcodeField", "ABC123456789", rules);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        _output.WriteLine("Barcode validation passed");
    }

    [Fact]
    public async Task ValidateFieldAsync_CustomBarcodeRule_WithInvalidBarcode_ShouldReturnError()
    {
        // Arrange
        var rules = new FieldValidationRules
        {
            CustomRules = new List<string> { "BARCODE" }
        };

        // Act
        var result = await _validationService.ValidateFieldAsync("BarcodeField", "invalid@barcode!", rules);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("INVALID_BARCODE", result.Errors[0].Code);
        _output.WriteLine($"Barcode validation error: {result.Errors[0].Message}");
    }

    [Fact]
    public async Task ValidateFieldsAsync_MultipleFields_WithMixedValidation_ShouldReturnCorrectResults()
    {
        // Arrange
        var fieldValues = new Dictionary<string, object?>
        {
            { "Name", "John Doe" },
            { "Email", "john@example.com" },
            { "Age", "25" },
            { "Phone", "+1234567890" }
        };

        var validationRules = new Dictionary<string, FieldValidationRules>
        {
            { "Name", new FieldValidationRules { IsRequired = true, MinLength = 2, MaxLength = 100 } },
            { "Email", new FieldValidationRules { RegexPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$" } },
            { "Age", new FieldValidationRules { MinValue = 18, MaxValue = 120 } },
            { "Phone", new FieldValidationRules { CustomRules = new List<string> { "MOBILE_NUMBER" } } }
        };

        // Act
        var result = await _validationService.ValidateFieldsAsync(fieldValues, validationRules);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(4, result.ValidFieldCount);
        Assert.Equal(4, result.TotalFieldCount);
        _output.WriteLine($"Multi-field validation: {result.ValidFieldCount}/{result.TotalFieldCount} fields valid");
    }

    [Fact]
    public async Task ValidateProductAsync_WithValidProduct_ShouldReturnValid()
    {
        // Arrange
        var shopId = Guid.NewGuid();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            ShopId = shopId,
            Name = "Test Product",
            UnitPrice = 10.99m,
            Barcode = "ABC123456789",
            Category = "Electronics",
            IsActive = true
        };

        // Act
        var result = await _validationService.ValidateProductAsync(product, shopId);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        _output.WriteLine("Product validation passed");
    }

    [Fact]
    public async Task ValidateProductAsync_WithInvalidProduct_ShouldReturnErrors()
    {
        // Arrange
        var shopId = Guid.NewGuid();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            ShopId = Guid.NewGuid(), // Different shop ID
            Name = "", // Empty name
            UnitPrice = -5, // Negative price
            IsActive = true
        };

        // Act
        var result = await _validationService.ValidateProductAsync(product, shopId);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.NotEmpty(result.BusinessRuleViolations);
        _output.WriteLine($"Product validation failed with {result.Errors.Count} errors and {result.BusinessRuleViolations.Count} business rule violations");
    }

    [Fact]
    public async Task ValidateCustomerAsync_WithValidCustomer_ShouldReturnValid()
    {
        // Arrange
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Name = "John Doe",
            MembershipNumber = "MEMBER001",
            Email = "john@example.com",
            Phone = "+1234567890",
            IsActive = true
        };

        // Act
        var result = await _validationService.ValidateCustomerAsync(customer);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        _output.WriteLine("Customer validation passed");
    }

    [Fact]
    public async Task ValidateCustomerAsync_WithInvalidCustomer_ShouldReturnErrors()
    {
        // Arrange
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Name = "", // Empty name
            MembershipNumber = "", // Empty membership number
            Email = "invalid-email", // Invalid email format
            Phone = "invalid-phone", // Invalid phone format
            IsActive = true
        };

        // Act
        var result = await _validationService.ValidateCustomerAsync(customer);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        _output.WriteLine($"Customer validation failed with {result.Errors.Count} errors");
    }

    [Fact]
    public async Task ValidateRealTimeAsync_WithValidInput_ShouldReturnPositiveFeedback()
    {
        // Arrange
        var context = new ValidationContext
        {
            EntityType = "UI",
            ContextData = new Dictionary<string, object> { { "ControlType", "TextBox" } }
        };

        // Act
        var result = await _validationService.ValidateRealTimeAsync("name", "John Doe", context);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(ValidationSeverity.Info, result.Severity);
        Assert.Contains("Valid", result.InstantFeedback ?? "");
        _output.WriteLine($"Real-time validation feedback: {result.InstantFeedback}");
    }

    [Fact]
    public async Task ValidateRealTimeAsync_WithInvalidInput_ShouldReturnErrorFeedback()
    {
        // Arrange
        var context = new ValidationContext
        {
            EntityType = "UI",
            ContextData = new Dictionary<string, object> { { "ControlType", "TextBox" } }
        };

        // Act
        var result = await _validationService.ValidateRealTimeAsync("name", "", context);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(ValidationSeverity.Error, result.Severity);
        Assert.NotNull(result.InstantFeedback);
        _output.WriteLine($"Real-time validation error: {result.InstantFeedback}");
    }

    [Fact]
    public async Task ValidateFormCompletionAsync_WithCompleteForm_ShouldReturnValid()
    {
        // Arrange
        var formData = new Dictionary<string, object?>
        {
            { "Name", "John Doe" },
            { "Email", "john@example.com" },
            { "Phone", "+1234567890" }
        };
        var requiredFields = new List<string> { "Name", "Email" };

        // Act
        var result = await _validationService.ValidateFormCompletionAsync(formData, requiredFields);

        // Assert
        Assert.True(result.IsValid);
        Assert.True(result.CanSubmit);
        Assert.Empty(result.MissingRequiredFields);
        Assert.Equal(100, result.CompletionPercentage);
        _output.WriteLine($"Form completion: {result.CompletionPercentage}% complete, can submit: {result.CanSubmit}");
    }

    [Fact]
    public async Task ValidateFormCompletionAsync_WithIncompleteForm_ShouldReturnInvalid()
    {
        // Arrange
        var formData = new Dictionary<string, object?>
        {
            { "Name", "John Doe" },
            { "Email", "" }, // Missing required field
            { "Phone", "+1234567890" }
        };
        var requiredFields = new List<string> { "Name", "Email" };

        // Act
        var result = await _validationService.ValidateFormCompletionAsync(formData, requiredFields);

        // Assert
        Assert.False(result.IsValid);
        Assert.False(result.CanSubmit);
        Assert.Single(result.MissingRequiredFields);
        Assert.Contains("Email", result.MissingRequiredFields);
        _output.WriteLine($"Form completion: {result.CompletionPercentage}% complete, missing fields: {string.Join(", ", result.MissingRequiredFields)}");
    }

    [Fact]
    public async Task GetLocalizedValidationMessageAsync_WithValidErrorCode_ShouldReturnLocalizedMessage()
    {
        // Arrange
        var parameters = new Dictionary<string, object>
        {
            { "fieldName", "TestField" }
        };

        // Act
        var message = await _validationService.GetLocalizedValidationMessageAsync("FIELD_REQUIRED", parameters, "en");

        // Assert
        Assert.NotNull(message);
        Assert.Contains("TestField", message);
        Assert.Contains("required", message.ToLowerInvariant());
        _output.WriteLine($"Localized message: {message}");
    }

    // ─── Sale Operation Validation Tests (Requirements 8.2, 10.1) ───────────────

    [Fact]
    public async Task ValidateSaleCreationAsync_WithValidInputs_ShouldReturnValid()
    {
        // Arrange
        var invoiceNumber = "INV-2024-001";
        var deviceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Act
        var result = await _validationService.ValidateSaleCreationAsync(invoiceNumber, deviceId, userId);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        _output.WriteLine("Sale creation validation passed for valid inputs");
    }

    [Fact]
    public async Task ValidateSaleCreationAsync_WithEmptyInvoiceNumber_ShouldReturnRequiredError()
    {
        // Arrange
        var deviceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Act
        var result = await _validationService.ValidateSaleCreationAsync("", deviceId, userId);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(SaleValidationErrorType.Required, result.Errors.First().Type);
        Assert.Equal("invoiceNumber", result.Errors.First().Field);
        _output.WriteLine($"Sale creation validation error: {result.Errors.First().Message}");
    }

    [Fact]
    public async Task ValidateSaleCreationAsync_WithEmptyDeviceId_ShouldReturnRequiredError()
    {
        // Arrange
        var invoiceNumber = "INV-2024-001";
        var userId = Guid.NewGuid();

        // Act
        var result = await _validationService.ValidateSaleCreationAsync(invoiceNumber, Guid.Empty, userId);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "deviceId" && e.Type == SaleValidationErrorType.Required);
        _output.WriteLine($"Device ID validation error: {result.Errors.First().Message}");
    }

    [Fact]
    public async Task ValidateSaleCreationAsync_WithEmptyUserId_ShouldReturnRequiredError()
    {
        // Arrange
        var invoiceNumber = "INV-2024-001";
        var deviceId = Guid.NewGuid();

        // Act
        var result = await _validationService.ValidateSaleCreationAsync(invoiceNumber, deviceId, Guid.Empty);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "userId" && e.Type == SaleValidationErrorType.Required);
        _output.WriteLine($"User ID validation error: {result.Errors.First().Message}");
    }

    [Fact]
    public async Task ValidateSaleCreationAsync_WithTooLongInvoiceNumber_ShouldReturnOutOfRangeError()
    {
        // Arrange
        var invoiceNumber = new string('X', 51); // 51 chars, exceeds 50 limit
        var deviceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Act
        var result = await _validationService.ValidateSaleCreationAsync(invoiceNumber, deviceId, userId);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Type == SaleValidationErrorType.OutOfRange);
        _output.WriteLine($"Invoice number length validation error: {result.Errors.First().Message}");
    }

    [Fact]
    public async Task ValidateProductAdditionAsync_WithValidInputs_ShouldReturnValid()
    {
        // Arrange
        var saleId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var quantity = 5;

        // Act
        var result = await _validationService.ValidateProductAdditionAsync(saleId, productId, quantity);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        _output.WriteLine("Product addition validation passed for valid inputs");
    }

    [Fact]
    public async Task ValidateProductAdditionAsync_WithZeroQuantity_ShouldReturnOutOfRangeError()
    {
        // Arrange
        var saleId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        // Act
        var result = await _validationService.ValidateProductAdditionAsync(saleId, productId, 0);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "quantity" && e.Type == SaleValidationErrorType.OutOfRange);
        _output.WriteLine($"Zero quantity validation error: {result.Errors.First().Message}");
    }

    [Fact]
    public async Task ValidateProductAdditionAsync_WithNegativeQuantity_ShouldReturnOutOfRangeError()
    {
        // Arrange
        var saleId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        // Act
        var result = await _validationService.ValidateProductAdditionAsync(saleId, productId, -1);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Type == SaleValidationErrorType.OutOfRange);
        _output.WriteLine($"Negative quantity validation error: {result.Errors.First().Message}");
    }

    [Fact]
    public async Task ValidateProductAdditionAsync_WithEmptySaleId_ShouldReturnRequiredError()
    {
        // Arrange
        var productId = Guid.NewGuid();

        // Act
        var result = await _validationService.ValidateProductAdditionAsync(Guid.Empty, productId, 1);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "saleId" && e.Type == SaleValidationErrorType.Required);
        _output.WriteLine($"Empty sale ID validation error: {result.Errors.First().Message}");
    }

    [Fact]
    public async Task ValidateWeightBasedProductAdditionAsync_WithValidInputs_ShouldReturnValid()
    {
        // Arrange
        var saleId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var weight = 1.5m;

        // Act
        var result = await _validationService.ValidateWeightBasedProductAdditionAsync(saleId, productId, weight);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        _output.WriteLine("Weight-based product addition validation passed");
    }

    [Fact]
    public async Task ValidateWeightBasedProductAdditionAsync_WithZeroWeight_ShouldReturnOutOfRangeError()
    {
        // Arrange
        var saleId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        // Act
        var result = await _validationService.ValidateWeightBasedProductAdditionAsync(saleId, productId, 0m);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "weight" && e.Type == SaleValidationErrorType.OutOfRange);
        _output.WriteLine($"Zero weight validation error: {result.Errors.First().Message}");
    }

    [Fact]
    public async Task ValidateWeightBasedProductAdditionAsync_WithNegativeWeight_ShouldReturnOutOfRangeError()
    {
        // Arrange
        var saleId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        // Act
        var result = await _validationService.ValidateWeightBasedProductAdditionAsync(saleId, productId, -0.5m);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Type == SaleValidationErrorType.OutOfRange);
        _output.WriteLine($"Negative weight validation error: {result.Errors.First().Message}");
    }

    [Fact]
    public async Task ValidateItemQuantityUpdateAsync_WithValidInputs_ShouldReturnValid()
    {
        // Arrange
        var saleItemId = Guid.NewGuid();
        var newQuantity = 3;

        // Act
        var result = await _validationService.ValidateItemQuantityUpdateAsync(saleItemId, newQuantity);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        _output.WriteLine("Item quantity update validation passed");
    }

    [Fact]
    public async Task ValidateItemQuantityUpdateAsync_WithZeroQuantity_ShouldReturnOutOfRangeError()
    {
        // Arrange
        var saleItemId = Guid.NewGuid();

        // Act
        var result = await _validationService.ValidateItemQuantityUpdateAsync(saleItemId, 0);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "newQuantity" && e.Type == SaleValidationErrorType.OutOfRange);
        _output.WriteLine($"Zero quantity update validation error: {result.Errors.First().Message}");
    }

    [Fact]
    public async Task ValidateItemQuantityUpdateAsync_WithEmptySaleItemId_ShouldReturnRequiredError()
    {
        // Act
        var result = await _validationService.ValidateItemQuantityUpdateAsync(Guid.Empty, 5);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "saleItemId" && e.Type == SaleValidationErrorType.Required);
        _output.WriteLine($"Empty sale item ID validation error: {result.Errors.First().Message}");
    }

    [Fact]
    public async Task ValidateSaleCompletionAsync_WithValidInputs_ShouldReturnValid()
    {
        // Arrange
        var saleId = Guid.NewGuid();
        var paymentMethod = PaymentMethod.Cash;
        var amountPaid = 100.00m;

        // Act
        var result = await _validationService.ValidateSaleCompletionAsync(saleId, paymentMethod, amountPaid);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        _output.WriteLine("Sale completion validation passed for valid inputs");
    }

    [Fact]
    public async Task ValidateSaleCompletionAsync_WithNegativeAmount_ShouldReturnOutOfRangeError()
    {
        // Arrange
        var saleId = Guid.NewGuid();

        // Act
        var result = await _validationService.ValidateSaleCompletionAsync(saleId, PaymentMethod.Cash, -10m);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "amountPaid" && e.Type == SaleValidationErrorType.OutOfRange);
        _output.WriteLine($"Negative amount validation error: {result.Errors.First().Message}");
    }

    [Fact]
    public async Task ValidateSaleCompletionAsync_WithZeroAmountForCash_ShouldReturnBusinessRuleError()
    {
        // Arrange
        var saleId = Guid.NewGuid();

        // Act
        var result = await _validationService.ValidateSaleCompletionAsync(saleId, PaymentMethod.Cash, 0m);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Type == SaleValidationErrorType.BusinessRule);
        _output.WriteLine($"Zero cash amount business rule error: {result.Errors.First().Message}");
    }

    [Fact]
    public async Task ValidateSaleCompletionAsync_WithEmptySaleId_ShouldReturnRequiredError()
    {
        // Act
        var result = await _validationService.ValidateSaleCompletionAsync(Guid.Empty, PaymentMethod.Cash, 50m);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "saleId" && e.Type == SaleValidationErrorType.Required);
        _output.WriteLine($"Empty sale ID completion validation error: {result.Errors.First().Message}");
    }

    [Fact]
    public async Task ValidateCustomerForSaleAsync_WithEmptyCustomerId_ShouldReturnRequiredError()
    {
        // Act
        var result = await _validationService.ValidateCustomerForSaleAsync(Guid.Empty);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "customerId" && e.Type == SaleValidationErrorType.Required);
        _output.WriteLine($"Empty customer ID validation error: {result.Errors.First().Message}");
    }

    [Fact]
    public async Task ValidateCustomerForSaleAsync_WithNonExistentCustomer_ShouldReturnCustomerInvalidError()
    {
        // Arrange - use a random Guid that won't exist in the in-memory DB
        var nonExistentCustomerId = Guid.NewGuid();

        // Act
        var result = await _validationService.ValidateCustomerForSaleAsync(nonExistentCustomerId);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Type == SaleValidationErrorType.CustomerInvalid);
        _output.WriteLine($"Non-existent customer validation error: {result.Errors.First().Message}");
    }

    [Fact]
    public void AggregateValidationResults_WithMultipleResults_ShouldCombineErrors()
    {
        // Arrange
        var result1 = new SaleValidationResult
        {
            IsValid = false,
            Errors = new List<SaleValidationError>
            {
                new() { Field = "invoiceNumber", Message = "Invoice number is required.", Type = SaleValidationErrorType.Required }
            }
        };
        var result2 = new SaleValidationResult
        {
            IsValid = false,
            Errors = new List<SaleValidationError>
            {
                new() { Field = "quantity", Message = "Quantity must be greater than zero.", Type = SaleValidationErrorType.OutOfRange }
            }
        };

        // Act
        var aggregated = _validationService.AggregateValidationResults(new[] { result1, result2 });

        // Assert
        Assert.False(aggregated.IsValid);
        Assert.Equal(2, aggregated.Errors.Count());
        Assert.Contains(aggregated.Errors, e => e.Field == "invoiceNumber");
        Assert.Contains(aggregated.Errors, e => e.Field == "quantity");
        _output.WriteLine($"Aggregated {aggregated.Errors.Count()} errors from 2 results");
    }

    [Fact]
    public void AggregateValidationResults_WithAllValidResults_ShouldReturnValid()
    {
        // Arrange
        var result1 = new SaleValidationResult { IsValid = true };
        var result2 = new SaleValidationResult { IsValid = true };

        // Act
        var aggregated = _validationService.AggregateValidationResults(new[] { result1, result2 });

        // Assert
        Assert.True(aggregated.IsValid);
        Assert.Empty(aggregated.Errors);
        _output.WriteLine("Aggregation of valid results returns valid");
    }

    [Fact]
    public void AggregateValidationResults_WithItemErrors_ShouldMergeItemErrors()
    {
        // Arrange
        var itemId1 = Guid.NewGuid();
        var itemId2 = Guid.NewGuid();

        var result1 = new SaleValidationResult
        {
            IsValid = false,
            ItemErrors = new Dictionary<Guid, IEnumerable<string>>
            {
                { itemId1, new[] { "Item 1 error A" } }
            }
        };
        var result2 = new SaleValidationResult
        {
            IsValid = false,
            ItemErrors = new Dictionary<Guid, IEnumerable<string>>
            {
                { itemId1, new[] { "Item 1 error B" } },
                { itemId2, new[] { "Item 2 error" } }
            }
        };

        // Act
        var aggregated = _validationService.AggregateValidationResults(new[] { result1, result2 });

        // Assert
        Assert.True(aggregated.ItemErrors.ContainsKey(itemId1));
        Assert.True(aggregated.ItemErrors.ContainsKey(itemId2));
        Assert.Equal(2, aggregated.ItemErrors[itemId1].Count());
        _output.WriteLine($"Aggregated item errors: {aggregated.ItemErrors.Count} items with errors");
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}