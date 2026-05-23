# Session Expiry Loop Fix: Preventing Unconditional Activity Updates

## Problem

The session expiry monitoring loop in `MainViewModel` was calling `UpdateActivityAsync()` unconditionally every 5 minutes, which:
- Reset the `LastActivityAt` timestamp on every timer tick
- Prevented inactivity-based session expiry from ever triggering
- Made sessions perpetually active regardless of actual user interaction
- Defeated the purpose of session timeout security

### Example Scenario
```
Time 0:00 - User logs in, LastActivityAt = 0:00
Time 5:00 - Timer tick, UpdateActivityAsync() called, LastActivityAt = 5:00 (reset!)
Time 10:00 - Timer tick, UpdateActivityAsync() called, LastActivityAt = 10:00 (reset!)
Time 15:00 - Timer tick, UpdateActivityAsync() called, LastActivityAt = 15:00 (reset!)
...
Result: Session never expires even if user is idle for hours
```

## Root Cause

In `src/Desktop/ViewModels/MainViewModel.cs` (lines 323-349):
```csharp
private async Task StartSessionExpiryMonitorAsync()
{
    using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
    while (await timer.WaitForNextTickAsync())
    {
        if (!_currentUserService.IsAuthenticated) break;

        try
        {
            var expired = await _currentUserService.IsSessionExpiredAsync();
            if (expired)
            {
                _currentUserService.ClearCurrentUser();
                SessionExpired?.Invoke(this, EventArgs.Empty);
                break;
            }

            // ❌ PROBLEM: Unconditional activity update
            await _currentUserService.UpdateActivityAsync();
        }
        catch { }
    }
}
```

The timer loop was designed to check for expiry AND keep the session alive, but this conflates two separate concerns:
1. **Checking expiry** (should happen periodically)
2. **Updating activity** (should happen only on real user interaction)

## Solution

### 1. Removed Unconditional Activity Update from Timer Loop

The timer now **only checks for expiry** and does NOT update activity:

```csharp
private async Task StartSessionExpiryMonitorAsync()
{
    if (_currentUserService == null) return;

    using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
    while (await timer.WaitForNextTickAsync())
    {
        if (!_currentUserService.IsAuthenticated) break;

        try
        {
            // Only check for expiry; do NOT update activity here
            var expired = await _currentUserService.IsSessionExpiredAsync();
            if (expired)
            {
                _currentUserService.ClearCurrentUser();
                SessionExpired?.Invoke(this, EventArgs.Empty);
                break;
            }
        }
        catch
        {
            // Don't crash the monitor on transient errors
        }
    }
}
```

### 2. Created Activity Detection Method

Added a new public method that ViewModels and commands can call when actual user activity occurs:

```csharp
/// <summary>
/// Called when actual user activity is detected (e.g., command execution, navigation).
/// Updates the session activity timestamp to prevent inactivity-based expiry.
/// </summary>
public async Task OnUserActivityDetectedAsync()
{
    if (_currentUserService?.IsAuthenticated == true)
    {
        try
        {
            await _currentUserService.UpdateActivityAsync();
        }
        catch
        {
            // Log but don't crash on activity update failures
        }
    }
}
```

### 3. Integrated Activity Detection into User Commands

Updated all relay commands that represent actual user interaction to call `OnUserActivityDetectedAsync()`:

**Commands Updated:**
- `LoadBusinessesAsync()` - User navigates to businesses
- `LoadShopsForBusinessAsync()` - User navigates to shops
- `RefreshDashboardAsync()` - User manually refreshes
- `SyncDataAsync()` - User initiates sync
- `SelectBusiness()` - User selects a business
- `SelectShop()` - User selects a shop

**Example:**
```csharp
[RelayCommand]
private async Task RefreshDashboardAsync()
{
    await OnUserActivityDetectedAsync();  // ✅ Update activity on user action
    await LoadDashboardData();
}
```

## How It Works Now

```
Time 0:00 - User logs in, LastActivityAt = 0:00
Time 0:30 - User clicks "Refresh Dashboard" → OnUserActivityDetectedAsync() → LastActivityAt = 0:30
Time 5:00 - Timer tick checks expiry (not expired, LastActivityAt = 0:30)
Time 5:15 - User clicks "Sync Data" → OnUserActivityDetectedAsync() → LastActivityAt = 5:15
Time 10:00 - Timer tick checks expiry (not expired, LastActivityAt = 5:15)
Time 30:00 - Timer tick checks expiry → EXPIRED (30 min idle) → Session ends
```

## Benefits

✅ **Proper inactivity detection** - Sessions expire when users are truly idle  
✅ **Security** - Prevents unauthorized access through abandoned sessions  
✅ **Separation of concerns** - Timer checks expiry; commands update activity  
✅ **Extensible** - Easy to add activity detection to other ViewModels/commands  
✅ **Configurable timeouts** - Role-based timeouts now work correctly  

## Files Modified

1. **src/Desktop/ViewModels/MainViewModel.cs**
   - Removed `await _currentUserService.UpdateActivityAsync()` from timer loop
   - Added `OnUserActivityDetectedAsync()` method
   - Updated 6 relay commands to call `OnUserActivityDetectedAsync()`

## Implementation Notes

### For Other ViewModels

If other ViewModels need to update session activity on user interaction, they should:

1. Inject `MainViewModel` or create a similar activity detection pattern
2. Call the activity update method when user performs actions
3. Example for SaleViewModel:
   ```csharp
   [RelayCommand]
   private async Task ProcessSaleAsync()
   {
       await OnUserActivityDetectedAsync();  // Update session activity
       // ... process sale logic
   }
   ```

### For Server-Side

The server's `SessionService` already has proper expiry logic:
- `IsSessionExpiredAsync()` checks `LastActivityAt` against role-based timeout
- `EndExpiredSessionsWithRoleBasedTimeoutsAsync()` cleans up expired sessions
- No changes needed on server side

## Testing Recommendations

1. **Manual Testing:**
   - Log in and wait without interacting
   - Verify session expires after role-based timeout (e.g., 30 min for Cashier)
   - Perform an action (e.g., refresh) and verify session stays active

2. **Automated Testing:**
   - Mock `ICurrentUserService` and verify `UpdateActivityAsync()` is called on commands
   - Verify timer loop does NOT call `UpdateActivityAsync()`
   - Test expiry detection with mocked session timestamps

3. **Integration Testing:**
   - Test with actual database sessions
   - Verify role-based timeouts work correctly
   - Test concurrent sessions with different roles

## References

- Session timeout security best practices
- OWASP: Session Management Cheat Sheet
- ASP.NET Core session state documentation
