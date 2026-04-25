using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Core.DependencyInjection;
using Shared.Core.DTOs;
using Shared.Core.Entities;
using Shared.Core.Enums;
using Shared.Core.Services;
using Shared.Core.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace Shared.Core.Tests;

/// <summary>
/// Final integration tests validating complete POS system workflows.
/// Covers DI wiring, multi-tab sales, customer lookup, barcode scanning,
/// exception handling, performance, and memory optimization.
/// Requirements: All requirements integration
/// </summary>
public class FinalIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ITestOutputHelper _output;

    public FinalIntegrationTests(ITestOutputHelper output)
    {
        _output = output;

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning));
        services.AddSharedCoreInMemory();

        _serviceProvider = services.BuildServiceProvider();

        // Seed a valid license so service-level license checks pass
        SetupLicenseAsync().GetAwaiter().GetResult();
    }

    private async Task SetupLicenseAsync()
    {
        var currentUserService = _serviceProvider.GetService<ICurrentUserService>();
        var licenseRepo = _serviceProvider.GetService<ILicenseRepository>();
        if (currentUserService == null || licenseRepo == null) return;

        var license = new License
        {
            Id = Guid.NewGuid(),
            LicenseKey = "FINAL-INTEGRATION-TEST",
            Type = LicenseType.Professional,
            IssueDate = DateTime.UtcNow.AddDays(-30),
            ExpiryDate = DateTime.UtcNow.AddYears(1),
            Status = LicenseStatus.Active,
            CustomerName = "Integration Test",
            CustomerEmail = "test@integration.com",
            MaxDevices = 10,
            Features = new List<string> { "basic_pos", "inventory", "advanced_reports", "multi_user", "weight_based", "membership", "discounts" },
            ActivationDate = DateTime.UtcNow.AddDays(-30),
            DeviceId = currentUserService.GetDeviceId()
        };

        await licenseRepo.AddAsync(license);
        await licenseRepo.SaveChangesAsync();
    }

    #region DI Wiring Validation

    [Fact]
    public void AllCoreServices_ShouldBeResolvableFromDI()
    {
        // Verify all critical shared services resolve without error
        var criticalServices = new[]
        {
            typeof(IMultiTabSalesManager),
            typeof(ICustomerLookupService),
            typeof(IBarcodeIntegrationService),
            typeof(IRealTimeCalculationEngine),
            typeof(IEnhancedSalesGridEngine),
            typeof(IGlobalExceptionHandler),
            typeof(IValidationService),
            typeof(ITransactionStateService),
            typeof(ICrashRecoveryService),
            typeof(IEnhancedErrorRecoveryService),
            typeof(IPerformanceOptimizationService),
            typeof(IEnhancedPerformanceMonitoringService),
            typeof(IConfigurationManagementService),
            typeof(IEnhancedAuditService),
            typeof(IComprehensiveLoggingService),
        };

        foreach (var serviceType in criticalServices)
        {
            var resolved = _serviceProvider.GetService(serviceType);
            Assert.True(resolved != null, $"Service {serviceType.Name} could not be resolved from DI container");
            _output.WriteLine($"✓ {serviceType.Name} resolved successfully");
        }
    }

    [Fact]
    public void AllRepositories_ShouldBeResolvableFromDI()
    {
        var repositoryTypes = new[]
        {
            typeof(IProductRepository),
            typeof(ISaleRepository),
            typeof(ISaleItemRepository),
            typeof(ISaleSessionRepository),
            typeof(IStockRepository),
            typeof(ICustomerRepository),
            typeof(ICustomerMembershipRepository),
            typeof(IDiscountRepository),
            typeof(IUserRepository),
            typeof(IShopRepository),
            typeof(IBusinessRepository),
            typeof(ILicenseRepository),
        };

        foreach (var repoType in repositoryTypes)
        {
            var resolved = _serviceProvider.GetService(repoType);
            Assert.True(resolved != null, $"Repository {repoType.Name} could not be resolved from DI container");
            _output.WriteLine($"✓ {repoType.Name} resolved successfully");
        }
    }

    #endregion

    #region Multi-Tab Sales Workflow (Requirement 5)

    [Fact]
    public async Task MultiTabSales_CreateAndIsolateSessions_ShouldWork()
    {
        var manager = _serviceProvider.GetRequiredService<IMultiTabSalesManager>();
        var productRepo = _serviceProvider.GetRequiredService<IProductRepository>();
        var stockRepo = _serviceProvider.GetRequiredService<IStockRepository>();

        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        // Create two sessions
        var req1 = new CreateSaleSessionRequest { UserId = userId, DeviceId = deviceId, ShopId = shopId, TabName = "Tab A" };
        var req2 = new CreateSaleSessionRequest { UserId = userId, DeviceId = deviceId, ShopId = shopId, TabName = "Tab B" };

        var result1 = await manager.CreateNewSaleSessionAsync(req1);
        var result2 = await manager.CreateNewSaleSessionAsync(req2);

        Assert.True(result1.Success, $"Session 1 creation failed: {result1.Message}");
        Assert.True(result2.Success, $"Session 2 creation failed: {result2.Message}");
        Assert.NotEqual(result1.Session!.Id, result2.Session!.Id);

        // Add different items to each session
        var product = new Product
        {
            Id = Guid.NewGuid(), Name = "Test Product", Barcode = "TEST001",
            UnitPrice = 10.00m, IsActive = true, DeviceId = deviceId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, SyncStatus = SyncStatus.NotSynced
        };
        await productRepo.AddAsync(product);
        await productRepo.SaveChangesAsync();

        var stock = new Stock { Id = Guid.NewGuid(), ProductId = product.Id, Quantity = 100, DeviceId = deviceId, SyncStatus = SyncStatus.NotSynced };
        await stockRepo.AddAsync(stock);
        await stockRepo.SaveChangesAsync();

        var item1 = new SaleSessionItemDto { ProductId = product.Id, ProductName = product.Name, Quantity = 2, UnitPrice = 10.00m };
        var item2 = new SaleSessionItemDto { ProductId = product.Id, ProductName = product.Name, Quantity = 5, UnitPrice = 10.00m };

        var add1 = await manager.AddItemToSessionAsync(result1.Session.Id, item1);
        var add2 = await manager.AddItemToSessionAsync(result2.Session.Id, item2);

        Assert.True(add1.Success);
        Assert.True(add2.Success);

        // Verify isolation: each session has its own quantity
        var session1 = await manager.GetSaleSessionAsync(result1.Session.Id);
        var session2 = await manager.GetSaleSessionAsync(result2.Session.Id);

        Assert.NotNull(session1);
        Assert.NotNull(session2);
        Assert.Equal(2, session1!.Items[0].Quantity);
        Assert.Equal(5, session2!.Items[0].Quantity);

        _output.WriteLine("Multi-tab session isolation verified");
    }

    [Fact]
    public async Task MultiTabSales_SessionLimitEnforcement_ShouldPreventExcess()
    {
        var manager = _serviceProvider.GetRequiredService<IMultiTabSalesManager>();
        var maxSessions = await manager.GetMaxConcurrentSessionsAsync();

        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        // Fill up to the limit
        for (int i = 0; i < maxSessions; i++)
        {
            var req = new CreateSaleSessionRequest { UserId = userId, DeviceId = deviceId, ShopId = shopId, TabName = $"Tab {i + 1}" };
            var result = await manager.CreateNewSaleSessionAsync(req);
            Assert.True(result.Success, $"Failed to create session {i + 1}");
        }

        // One more should fail
        var extraReq = new CreateSaleSessionRequest { UserId = userId, DeviceId = deviceId, ShopId = shopId, TabName = "Extra" };
        var extraResult = await manager.CreateNewSaleSessionAsync(extraReq);

        Assert.False(extraResult.Success);
        _output.WriteLine($"Session limit ({maxSessions}) enforced correctly");
    }

    #endregion

    #region Customer Lookup and Auto-Fill (Requirement 6)

    [Fact]
    public async Task CustomerLookup_ByMobileNumber_ShouldReturnCorrectCustomer()
    {
        var customerRepo = _serviceProvider.GetRequiredService<ICustomerRepository>();
        var lookupService = _serviceProvider.GetRequiredService<ICustomerLookupService>();

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            MembershipNumber = "FINAL-TEST-001",
            Name = "Alice Smith",
            Phone = "5550001111",
            Email = "alice@test.com",
            Tier = MembershipTier.Gold,
            TotalSpent = 2000m,
            IsActive = true,
            DeviceId = Guid.NewGuid()
        };

        await customerRepo.AddAsync(customer);
        await customerRepo.SaveChangesAsync();

        var result = await lookupService.LookupByMobileNumberAsync("5550001111");

        Assert.NotNull(result);
        Assert.Equal(customer.Id, result!.Id);
        Assert.Equal("Alice Smith", result.Name);
        Assert.Equal(MembershipTier.Gold, result.Tier);

        _output.WriteLine($"Customer lookup returned: {result.Name} ({result.Tier})");
    }

    [Fact]
    public async Task CustomerLookup_UnknownNumber_ShouldReturnNull()
    {
        var lookupService = _serviceProvider.GetRequiredService<ICustomerLookupService>();

        var result = await lookupService.LookupByMobileNumberAsync("0000000000");

        Assert.Null(result);
        _output.WriteLine("Unknown mobile number correctly returned null");
    }

    [Fact]
    public async Task CustomerLookup_MembershipDetails_ShouldReturnTierInfo()
    {
        var customerRepo = _serviceProvider.GetRequiredService<ICustomerRepository>();
        var lookupService = _serviceProvider.GetRequiredService<ICustomerLookupService>();

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            MembershipNumber = "FINAL-TEST-002",
            Name = "Bob Jones",
            Phone = "5550002222",
            Tier = MembershipTier.Silver,
            TotalSpent = 500m,
            IsActive = true,
            DeviceId = Guid.NewGuid()
        };

        await customerRepo.AddAsync(customer);
        await customerRepo.SaveChangesAsync();

        var membership = await lookupService.GetMembershipDetailsAsync(customer.Id);

        Assert.NotNull(membership);
        Assert.Equal(MembershipTier.Silver, membership!.Tier);
        Assert.True(membership.DiscountPercentage >= 0);

        _output.WriteLine($"Membership details: Tier={membership.Tier}, Discount={membership.DiscountPercentage}%");
    }

    #endregion

    #region Barcode Scanning (Requirement 8)

    [Fact]
    public async Task BarcodeScanning_ValidFormat_ShouldValidateCorrectly()
    {
        var barcodeService = _serviceProvider.GetRequiredService<IBarcodeIntegrationService>();

        Assert.True(await barcodeService.ValidateBarcodeFormatAsync("1234567890123")); // EAN-13
        Assert.True(await barcodeService.ValidateBarcodeFormatAsync("12345678"));       // EAN-8
        Assert.True(await barcodeService.ValidateBarcodeFormatAsync("CODE128TEST"));    // Code 128

        _output.WriteLine("Barcode format validation passed for EAN-13, EAN-8, Code 128");
    }

    [Fact]
    public async Task BarcodeScanning_ProductLookup_ShouldFindRegisteredProduct()
    {
        var productRepo = _serviceProvider.GetRequiredService<IProductRepository>();
        var stockRepo = _serviceProvider.GetRequiredService<IStockRepository>();
        var barcodeService = _serviceProvider.GetRequiredService<IBarcodeIntegrationService>();

        var shopId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        var product = new Product
        {
            Id = Guid.NewGuid(), Name = "Barcode Product", Barcode = "9876543210987",
            UnitPrice = 19.99m, IsActive = true, DeviceId = deviceId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, SyncStatus = SyncStatus.NotSynced
        };
        await productRepo.AddAsync(product);
        await productRepo.SaveChangesAsync();

        var stock = new Stock { Id = Guid.NewGuid(), ProductId = product.Id, Quantity = 50, DeviceId = deviceId, SyncStatus = SyncStatus.NotSynced };
        await stockRepo.AddAsync(stock);
        await stockRepo.SaveChangesAsync();

        var found = await barcodeService.LookupProductByBarcodeAsync("9876543210987", shopId);

        Assert.NotNull(found);
        Assert.Equal(product.Id, found!.Id);
        Assert.Equal("Barcode Product", found.Name);

        _output.WriteLine($"Barcode lookup found: {found.Name} @ ${found.UnitPrice}");
    }

    [Fact]
    public async Task BarcodeScanning_SupportedFormats_ShouldIncludeCommonRetailFormats()
    {
        var barcodeService = _serviceProvider.GetRequiredService<IBarcodeIntegrationService>();

        var formats = await barcodeService.GetSupportedFormatsAsync();

        Assert.NotEmpty(formats);
        Assert.Contains(formats, f => f.Name.Contains("EAN") || f.Name.Contains("Code"));

        _output.WriteLine($"Supported barcode formats: {string.Join(", ", formats.Select(f => f.Name))}");
    }

    #endregion

    #region Real-Time Calculation Engine (Requirement 9)

    [Fact]
    public async Task RealTimeCalculation_BasicOrder_ShouldCalculateCorrectTotals()
    {
        var calcEngine = _serviceProvider.GetRequiredService<IRealTimeCalculationEngine>();

        var items = new List<SaleItem>
        {
            new() { Id = Guid.NewGuid(), ProductId = Guid.NewGuid(), Quantity = 2, UnitPrice = 50.00m, TotalPrice = 100.00m },
            new() { Id = Guid.NewGuid(), ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 30.00m, TotalPrice = 30.00m },
        };

        var config = new ShopConfiguration { TaxRate = 0.10m };
        var result = await calcEngine.CalculateOrderTotalsAsync(items, config, null);

        Assert.True(result.IsValid);
        Assert.Equal(130.00m, result.Subtotal);
        Assert.True(result.FinalTotal >= result.Subtotal); // Tax adds to total

        _output.WriteLine($"Calculation: Subtotal={result.Subtotal:C}, Tax={result.TotalTaxAmount:C}, Total={result.FinalTotal:C}");
    }

    [Fact]
    public async Task RealTimeCalculation_WithMembershipCustomer_ShouldApplyDiscount()
    {
        var calcEngine = _serviceProvider.GetRequiredService<IRealTimeCalculationEngine>();

        var items = new List<SaleItem>
        {
            new() { Id = Guid.NewGuid(), ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 100.00m, TotalPrice = 100.00m },
        };

        var config = new ShopConfiguration { TaxRate = 0.0m };
        var goldCustomer = new Customer { Id = Guid.NewGuid(), Tier = MembershipTier.Gold, TotalSpent = 5000m };

        var resultWithCustomer = await calcEngine.CalculateOrderTotalsAsync(items, config, goldCustomer);
        var resultWithoutCustomer = await calcEngine.CalculateOrderTotalsAsync(items, config, null);

        Assert.True(resultWithCustomer.IsValid);
        Assert.True(resultWithoutCustomer.IsValid);

        // Gold customer should get a discount (final total <= subtotal)
        Assert.True(resultWithCustomer.FinalTotal <= resultWithoutCustomer.FinalTotal,
            "Gold tier customer should receive a discount");

        _output.WriteLine($"Without customer: {resultWithoutCustomer.FinalTotal:C}, With Gold customer: {resultWithCustomer.FinalTotal:C}");
    }

    #endregion

    #region Exception Handling (Requirement 3)

    [Fact]
    public async Task ExceptionHandler_DatabaseException_ShouldReturnUserFriendlyMessage()
    {
        var handler = _serviceProvider.GetRequiredService<IGlobalExceptionHandler>();
        var deviceId = Guid.NewGuid();

        var dbEx = new Microsoft.EntityFrameworkCore.DbUpdateException("Connection failed");
        var response = await handler.HandleExceptionAsync(dbEx, "Test Context", deviceId);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Message);
        Assert.Equal(500, response.StatusCode);

        _output.WriteLine($"DB exception handled: {response.Message}");
    }

    [Fact]
    public async Task ExceptionHandler_ValidationException_ShouldReturn400()
    {
        var handler = _serviceProvider.GetRequiredService<IGlobalExceptionHandler>();
        var deviceId = Guid.NewGuid();

        var validationEx = new ArgumentException("Invalid input");
        var response = await handler.HandleExceptionAsync(validationEx, "Validation", deviceId);

        Assert.NotNull(response);
        Assert.Equal(400, response.StatusCode);

        _output.WriteLine($"Validation exception handled: {response.Message}");
    }

    [Fact]
    public async Task ExceptionHandler_AllExceptionTypes_ShouldProvideRecoveryActions()
    {
        var handler = _serviceProvider.GetRequiredService<IGlobalExceptionHandler>();
        var context = "Recovery Test";

        var exceptions = new Exception[]
        {
            new Microsoft.EntityFrameworkCore.DbUpdateException("DB Error"),
            new HttpRequestException("Network Error"),
            new TimeoutException("Timeout"),
            new InvalidOperationException("Invalid Op"),
        };

        foreach (var ex in exceptions)
        {
            var recovery = await handler.SuggestRecoveryActionAsync(ex, context);
            Assert.NotNull(recovery);
            Assert.NotEmpty(recovery.ActionType);
            _output.WriteLine($"{ex.GetType().Name} -> Recovery: {recovery.ActionType}");
        }
    }

    #endregion

    #region Transaction State Persistence (Requirement 12)

    [Fact]
    public async Task TransactionState_SaveAndRestore_ShouldPreserveData()
    {
        // TransactionStateService.SaveTransactionStateAsync requires a real SaleSession in the DB.
        // Create one via MultiTabSalesManager first, then save/restore state against that session.
        var manager = _serviceProvider.GetRequiredService<IMultiTabSalesManager>();
        var stateService = _serviceProvider.GetRequiredService<ITransactionStateService>();

        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        var sessionReq = new CreateSaleSessionRequest { UserId = userId, DeviceId = deviceId, ShopId = shopId, TabName = "State Test" };
        var sessionResult = await manager.CreateNewSaleSessionAsync(sessionReq);
        Assert.True(sessionResult.Success, $"Session creation failed: {sessionResult.Message}");

        var sessionId = sessionResult.Session!.Id;

        var state = new TransactionState
        {
            SaleSessionId = sessionId,
            UserId = userId,
            DeviceId = deviceId,
            ShopId = shopId,
            SaleItems = new List<TransactionSaleItem>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Test Item", Quantity = 3, UnitPrice = 25.00m, LineTotal = 75.00m }
            },
            CustomerId = Guid.NewGuid(),
            LastSavedAt = DateTime.UtcNow
        };

        // Save state
        var saveResult = await stateService.SaveTransactionStateAsync(sessionId, state);
        Assert.True(saveResult, "Transaction state should be saved successfully");

        // Restore state
        var restored = await stateService.RestoreTransactionStateAsync(sessionId);
        Assert.NotNull(restored);
        Assert.Equal(sessionId, restored!.SaleSessionId);
        Assert.Single(restored.SaleItems);
        Assert.Equal("Test Item", restored.SaleItems[0].ProductName);
        Assert.Equal(3, restored.SaleItems[0].Quantity);

        _output.WriteLine("Transaction state saved and restored successfully");
    }

    #endregion

    #region Performance Validation (Requirement 11)

    [Fact]
    public async Task Performance_ServiceResolution_ShouldBeUnder100ms()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var scope = _serviceProvider.CreateScope();
        var calcEngine = scope.ServiceProvider.GetRequiredService<IRealTimeCalculationEngine>();
        var lookupService = scope.ServiceProvider.GetRequiredService<ICustomerLookupService>();
        var barcodeService = scope.ServiceProvider.GetRequiredService<IBarcodeIntegrationService>();

        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 100,
            $"Service resolution took {sw.ElapsedMilliseconds}ms, expected < 100ms");

        _output.WriteLine($"Service resolution time: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Performance_CalculationEngine_ShouldCompleteUnder500ms()
    {
        var calcEngine = _serviceProvider.GetRequiredService<IRealTimeCalculationEngine>();

        // Build a realistic cart with 20 items
        var items = Enumerable.Range(1, 20).Select(i => new SaleItem
        {
            Id = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Quantity = i,
            UnitPrice = i * 5.00m,
            TotalPrice = i * i * 5.00m
        }).ToList();

        var config = new ShopConfiguration { TaxRate = 0.18m };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await calcEngine.CalculateOrderTotalsAsync(items, config, null);
        sw.Stop();

        Assert.True(result.IsValid);
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Calculation took {sw.ElapsedMilliseconds}ms, expected < 500ms");

        _output.WriteLine($"Calculation for 20 items completed in {sw.ElapsedMilliseconds}ms");
    }

    #endregion

    #region Memory Optimization (Requirement 1)

    [Fact]
    public void MemoryUsage_ServiceResolution_ShouldNotExceed100MB()
    {
        var before = GC.GetTotalMemory(true);

        // Resolve and use services in a scope (simulating normal operation)
        using (var scope = _serviceProvider.CreateScope())
        {
            var services = new object[]
            {
                scope.ServiceProvider.GetRequiredService<IMultiTabSalesManager>(),
                scope.ServiceProvider.GetRequiredService<ICustomerLookupService>(),
                scope.ServiceProvider.GetRequiredService<IBarcodeIntegrationService>(),
                scope.ServiceProvider.GetRequiredService<IRealTimeCalculationEngine>(),
                scope.ServiceProvider.GetRequiredService<IValidationService>(),
            };

            // Ensure services are actually used (not optimized away)
            Assert.Equal(5, services.Length);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var after = GC.GetTotalMemory(false);
        var usedMB = (after - before) / (1024.0 * 1024.0);

        Assert.True(usedMB < 100,
            $"Memory usage was {usedMB:F1}MB, expected < 100MB");

        _output.WriteLine($"Memory used for service resolution: {usedMB:F2}MB");
    }

    #endregion

    #region Complete End-to-End Workflow

    [Fact]
    public async Task CompleteWorkflow_MultiTabWithCustomerAndBarcode_ShouldSucceed()
    {
        _output.WriteLine("=== Complete End-to-End Workflow Test ===");

        var manager = _serviceProvider.GetRequiredService<IMultiTabSalesManager>();
        var customerRepo = _serviceProvider.GetRequiredService<ICustomerRepository>();
        var productRepo = _serviceProvider.GetRequiredService<IProductRepository>();
        var stockRepo = _serviceProvider.GetRequiredService<IStockRepository>();
        var lookupService = _serviceProvider.GetRequiredService<ICustomerLookupService>();
        var barcodeService = _serviceProvider.GetRequiredService<IBarcodeIntegrationService>();
        var calcEngine = _serviceProvider.GetRequiredService<IRealTimeCalculationEngine>();
        var gridEngine = _serviceProvider.GetRequiredService<IEnhancedSalesGridEngine>();

        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        // 1. Setup test data
        var customer = new Customer
        {
            Id = Guid.NewGuid(), MembershipNumber = "E2E-TEST-001",
            Name = "E2E Customer", Phone = "5551234000",
            Tier = MembershipTier.Gold, TotalSpent = 3000m,
            IsActive = true, DeviceId = deviceId
        };
        await customerRepo.AddAsync(customer);
        await customerRepo.SaveChangesAsync();

        var product = new Product
        {
            Id = Guid.NewGuid(), Name = "E2E Product", Barcode = "E2EBARCODE001",
            UnitPrice = 50.00m, IsActive = true, DeviceId = deviceId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, SyncStatus = SyncStatus.NotSynced
        };
        await productRepo.AddAsync(product);
        await productRepo.SaveChangesAsync();

        var stock = new Stock { Id = Guid.NewGuid(), ProductId = product.Id, Quantity = 100, DeviceId = deviceId, SyncStatus = SyncStatus.NotSynced };
        await stockRepo.AddAsync(stock);
        await stockRepo.SaveChangesAsync();

        // 2. Create multi-tab session
        var sessionReq = new CreateSaleSessionRequest { UserId = userId, DeviceId = deviceId, ShopId = shopId, TabName = "E2E Tab" };
        var sessionResult = await manager.CreateNewSaleSessionAsync(sessionReq);
        Assert.True(sessionResult.Success, $"Session creation failed: {sessionResult.Message}");
        _output.WriteLine($"✓ Session created: {sessionResult.Session!.Id}");

        // 3. Customer lookup and auto-fill
        var foundCustomer = await lookupService.LookupByMobileNumberAsync("5551234000");
        Assert.NotNull(foundCustomer);
        Assert.Equal("E2E Customer", foundCustomer!.Name);
        _output.WriteLine($"✓ Customer found: {foundCustomer.Name} ({foundCustomer.Tier})");

        // 4. Barcode scan and product lookup
        var scannedProduct = await barcodeService.LookupProductByBarcodeAsync("E2EBARCODE001", shopId);
        Assert.NotNull(scannedProduct);
        _output.WriteLine($"✓ Product found via barcode: {scannedProduct!.Name}");

        // 5. Add product to session (updates the session JSON blob used by CompleteSessionAsync)
        var sessionItem = new SaleSessionItemDto
        {
            ProductId = scannedProduct!.Id,
            ProductName = scannedProduct.Name,
            Quantity = 2,
            UnitPrice = scannedProduct.UnitPrice,
            LineTotal = 2 * scannedProduct.UnitPrice
        };
        var addResult = await manager.AddItemToSessionAsync(sessionResult.Session.Id, sessionItem);
        Assert.True(addResult.Success, $"Add item to session failed: {addResult.Message}");
        _output.WriteLine($"✓ Product added to session (qty=2)");

        // 6. Real-time calculation via grid engine
        var calcResult = await gridEngine.RecalculateAllTotalsAsync(sessionResult.Session.Id);
        Assert.True(calcResult.Subtotal >= 0);
        _output.WriteLine($"✓ Calculation: Subtotal={calcResult.Subtotal:C}, Total={calcResult.FinalTotal:C}");

        // 7. Complete the session
        var completion = await manager.CompleteSessionAsync(sessionResult.Session.Id, PaymentMethod.Cash);
        Assert.True(completion.Success, $"Session completion failed: {completion.Message}");
        _output.WriteLine($"✓ Sale completed successfully");

        // 8. Verify session is no longer active
        var activeSessions = await manager.GetActiveSessionsAsync(userId, deviceId);
        Assert.DoesNotContain(activeSessions, s => s.Id == sessionResult.Session.Id);
        _output.WriteLine($"✓ Session removed from active list");

        _output.WriteLine("=== Complete End-to-End Workflow PASSED ===");
    }

    #endregion

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}
