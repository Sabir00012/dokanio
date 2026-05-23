# Session Activity Integration Guide

## Overview

This guide explains how to integrate session activity tracking into other ViewModels and services in the Dokanio POS system.

## Core Principle

**Session activity should be updated ONLY when actual user interaction occurs**, not on timers or background operations.

---

## Integration Patterns

### Pattern 1: Direct Integration (Recommended for Desktop ViewModels)

For ViewModels that have access to `MainViewModel`, call `OnUserActivityDetectedAsync()` directly:

```csharp
public partial class SaleViewModel : BaseViewModel
{
    private readonly MainViewModel _mainViewModel;
    private readonly ISaleService _saleService;
    
    public SaleViewModel(MainViewModel mainViewModel, ISaleService saleService)
    {
        _mainViewModel = mainViewModel;
        _saleService = saleService;
    }
    
    [RelayCommand]
    private async Task ProcessSaleAsync()
    {
        // Update session activity on user action
        await _mainViewModel.OnUserActivityDetectedAsync();
        
        // Perform the actual sale processing
        var result = await _saleService.ProcessSaleAsync(...);
        // ...
    }
    
    [RelayCommand]
    private async Task AddItemToSaleAsync(Product product)
    {
        // Update session activity
        await _mainViewModel.OnUserActivityDetectedAsync();
        
        // Add item logic
        // ...
    }
}
```

### Pattern 2: Service-Level Integration

For services that need to track activity, inject `ICurrentUserService` and call `UpdateActivityAsync()`:

```csharp
public class SaleService : ISaleService
{
    private readonly ICurrentUserService _currentUserService;
    private readonly ISaleRepository _saleRepository;
    
    public SaleService(ICurrentUserService currentUserService, ISaleRepository saleRepository)
    {
        _currentUserService = currentUserService;
        _saleRepository = saleRepository;
    }
    
    public async Task<SaleResult> ProcessSaleAsync(Sale sale)
    {
        // Update session activity on significant operations
        await _currentUserService.UpdateActivityAsync();
        
        // Process the sale
        var result = await _saleRepository.AddAsync(sale);
        await _saleRepository.SaveChangesAsync();
        
        return result;
    }
}
```

### Pattern 3: Middleware/Interceptor Pattern

For cross-cutting concerns, create an activity tracking decorator:

```csharp
public class ActivityTrackingCommandDecorator<TCommand> : ICommandHandler<TCommand>
{
    private readonly ICommandHandler<TCommand> _inner;
    private readonly ICurrentUserService _currentUserService;
    
    public ActivityTrackingCommandDecorator(
        ICommandHandler<TCommand> inner,
        ICurrentUserService currentUserService)
    {
        _inner = inner;
        _currentUserService = currentUserService;
    }
    
    public async Task HandleAsync(TCommand command)
    {
        // Update activity before handling command
        await _currentUserService.UpdateActivityAsync();
        
        // Delegate to actual handler
        await _inner.HandleAsync(command);
    }
}
```

---

## ViewModels That Should Track Activity

### High Priority (User-Facing Operations)

These ViewModels should track activity on all user-triggered commands:

1. **SaleViewModel**
   - `ProcessSaleAsync()` - Processing a sale
   - `AddItemToSaleAsync()` - Adding items
   - `RemoveItemAsync()` - Removing items
   - `ApplyDiscountAsync()` - Applying discounts
   - `ProcessPaymentAsync()` - Processing payment

2. **InventoryViewModel** (if exists)
   - `UpdateStockAsync()` - Stock updates
   - `AddProductAsync()` - Adding products
   - `EditProductAsync()` - Editing products
   - `CheckExpiryAsync()` - Checking expiry dates

3. **ReportsViewModel**
   - `GenerateReportAsync()` - Generating reports
   - `ExportDataAsync()` - Exporting data
   - `RefreshReportAsync()` - Refreshing reports

4. **UserManagementViewModel**
   - `CreateUserAsync()` - Creating users
   - `EditUserAsync()` - Editing users
   - `ChangePasswordAsync()` - Changing passwords
   - `ResetUserAsync()` - Resetting users

5. **BusinessManagementViewModel**
   - `CreateBusinessAsync()` - Creating business
   - `EditBusinessAsync()` - Editing business
   - `CreateShopAsync()` - Creating shop
   - `EditShopAsync()` - Editing shop

### Medium Priority (Administrative Operations)

These can track activity on significant operations:

1. **ConfigurationViewModel**
   - `SaveConfigurationAsync()` - Saving config
   - `ResetConfigurationAsync()` - Resetting config

2. **AdvancedReportsViewModel**
   - `GenerateAdvancedReportAsync()` - Complex reports
   - `ScheduleReportAsync()` - Scheduling reports

### Low Priority (Background Operations)

These should NOT track activity (they're not user-triggered):

- Dashboard refresh timers
- Sync background tasks
- Notification polling
- Cache refresh operations

---

## Implementation Checklist

### For Each ViewModel

- [ ] Identify all `[RelayCommand]` methods that represent user actions
- [ ] Add `await OnUserActivityDetectedAsync()` or `await _currentUserService.UpdateActivityAsync()` at the start
- [ ] Exclude background/timer-based operations
- [ ] Test that session activity is updated on user action
- [ ] Test that session expires correctly when idle

### Example Implementation

```csharp
public partial class ProductViewModel : BaseViewModel
{
    private readonly MainViewModel _mainViewModel;
    private readonly IProductService _productService;
    
    public ProductViewModel(MainViewModel mainViewModel, IProductService productService)
    {
        _mainViewModel = mainViewModel;
        _productService = productService;
    }
    
    // ✅ Track activity - user is creating a product
    [RelayCommand]
    private async Task CreateProductAsync()
    {
        await _mainViewModel.OnUserActivityDetectedAsync();
        // ... create product logic
    }
    
    // ✅ Track activity - user is editing a product
    [RelayCommand]
    private async Task EditProductAsync(Product product)
    {
        await _mainViewModel.OnUserActivityDetectedAsync();
        // ... edit product logic
    }
    
    // ✅ Track activity - user is deleting a product
    [RelayCommand]
    private async Task DeleteProductAsync(Product product)
    {
        await _mainViewModel.OnUserActivityDetectedAsync();
        // ... delete product logic
    }
    
    // ❌ Don't track activity - background operation
    private async Task RefreshProductListAsync()
    {
        // This is called by a timer, not user action
        // Don't update activity here
        var products = await _productService.GetAllProductsAsync();
        // ...
    }
}
```

---

## Testing Activity Tracking

### Unit Test Example

```csharp
[Fact]
public async Task ProcessSaleAsync_UpdatesSessionActivity()
{
    // Arrange
    var mockCurrentUserService = new Mock<ICurrentUserService>();
    var mockSaleService = new Mock<ISaleService>();
    var viewModel = new SaleViewModel(mockCurrentUserService.Object, mockSaleService.Object);
    
    // Act
    await viewModel.ProcessSaleCommand.ExecuteAsync(null);
    
    // Assert
    mockCurrentUserService.Verify(
        x => x.UpdateActivityAsync(),
        Times.Once,
        "UpdateActivityAsync should be called on user action");
}
```

### Integration Test Example

```csharp
[Fact]
public async Task SessionExpires_AfterInactivity()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddSharedCoreInMemory();
    var provider = services.BuildServiceProvider();
    
    var currentUserService = provider.GetRequiredService<ICurrentUserService>();
    var sessionService = provider.GetRequiredService<ISessionService>();
    
    // Create a session
    var user = new User { Id = Guid.NewGuid(), Username = "test" };
    var session = await sessionService.CreateSessionAsync(user.Id);
    currentUserService.SetCurrentUser(user, session);
    
    // Act - Simulate 30 minutes of inactivity
    await Task.Delay(TimeSpan.FromSeconds(1)); // Simulate time passing
    var expired = await currentUserService.IsSessionExpiredAsync(inactivityTimeoutMinutes: 0);
    
    // Assert
    Assert.True(expired, "Session should expire after inactivity timeout");
}
```

---

## Common Mistakes to Avoid

### ❌ Mistake 1: Tracking Activity in Background Operations

```csharp
// WRONG - Don't do this!
private async Task RefreshDashboardTimerAsync()
{
    await _mainViewModel.OnUserActivityDetectedAsync(); // ❌ Wrong!
    // ... refresh logic
}
```

**Why:** Background timers are not user actions. They should not reset the inactivity timer.

### ❌ Mistake 2: Tracking Activity Multiple Times

```csharp
// WRONG - Don't do this!
[RelayCommand]
private async Task ProcessSaleAsync()
{
    await _mainViewModel.OnUserActivityDetectedAsync(); // ✅ Once here
    
    var result = await _saleService.ProcessSaleAsync(...);
    
    await _mainViewModel.OnUserActivityDetectedAsync(); // ❌ Not again!
}
```

**Why:** Multiple updates are wasteful. One update per user action is sufficient.

### ❌ Mistake 3: Tracking Activity in Constructors

```csharp
// WRONG - Don't do this!
public SaleViewModel(MainViewModel mainViewModel)
{
    _mainViewModel = mainViewModel;
    _ = _mainViewModel.OnUserActivityDetectedAsync(); // ❌ Wrong!
}
```

**Why:** ViewModel construction is not a user action.

### ✅ Correct Pattern

```csharp
// RIGHT - Do this!
[RelayCommand]
private async Task ProcessSaleAsync()
{
    // Update activity once at the start of user action
    await _mainViewModel.OnUserActivityDetectedAsync();
    
    // Perform the actual operation
    var result = await _saleService.ProcessSaleAsync(...);
    
    // Don't update activity again
}
```

---

## Troubleshooting

### Issue: Session expires too quickly

**Cause:** Activity is not being tracked on user actions

**Solution:**
1. Verify `OnUserActivityDetectedAsync()` is called in all user-triggered commands
2. Check that the method is being awaited properly
3. Verify `ICurrentUserService.IsAuthenticated` is true

### Issue: Session never expires

**Cause:** Activity is being updated too frequently (e.g., in timers)

**Solution:**
1. Remove activity updates from background operations
2. Verify timer loop does NOT call `UpdateActivityAsync()`
3. Check that only user-triggered commands update activity

### Issue: Activity updates fail silently

**Cause:** Exceptions in `UpdateActivityAsync()` are being caught

**Solution:**
1. Add logging to `OnUserActivityDetectedAsync()`
2. Check database connectivity
3. Verify session is still valid

---

## Migration Path

### Phase 1: Core ViewModels (Week 1)
- [ ] SaleViewModel
- [ ] MainViewModel (already done)

### Phase 2: Management ViewModels (Week 2)
- [ ] UserManagementViewModel
- [ ] BusinessManagementViewModel
- [ ] ProductViewModel

### Phase 3: Reporting ViewModels (Week 3)
- [ ] ReportsViewModel
- [ ] AdvancedReportsViewModel
- [ ] AIInventoryViewModel

### Phase 4: Mobile App (Week 4)
- [ ] Implement same pattern in Mobile ViewModels
- [ ] Test on Android/iOS

---

## References

- Session Management Best Practices
- OWASP Session Management Cheat Sheet
- ASP.NET Core Session State Documentation
- Dokanio Architecture Guide
