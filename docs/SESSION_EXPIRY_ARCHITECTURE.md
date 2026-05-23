# Session Expiry Architecture: Before and After

## Before (Broken)

```
┌─────────────────────────────────────────────────────────────────┐
│                    MainViewModel                                │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │  StartSessionExpiryMonitorAsync()                        │  │
│  │                                                          │  │
│  │  Timer: Every 5 minutes                                 │  │
│  │  ├─ Check: IsSessionExpiredAsync()                      │  │
│  │  │  └─ Compare LastActivityAt vs timeout               │  │
│  │  │                                                      │  │
│  │  └─ ❌ PROBLEM: UpdateActivityAsync()                   │  │
│  │     └─ Sets LastActivityAt = DateTime.UtcNow           │  │
│  │     └─ Resets the inactivity timer!                    │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                 │
│  User Commands (RefreshDashboard, SyncData, etc.)             │
│  └─ No activity tracking                                       │
└─────────────────────────────────────────────────────────────────┘

Result: Session NEVER expires due to inactivity
```

### Timeline Example (Before)
```
0:00  User logs in
      LastActivityAt = 0:00
      
5:00  Timer tick
      IsSessionExpiredAsync() → Not expired (5 min < 30 min timeout)
      UpdateActivityAsync() → LastActivityAt = 5:00 ❌
      
10:00 Timer tick
      IsSessionExpiredAsync() → Not expired (5 min < 30 min timeout)
      UpdateActivityAsync() → LastActivityAt = 10:00 ❌
      
15:00 Timer tick
      IsSessionExpiredAsync() → Not expired (5 min < 30 min timeout)
      UpdateActivityAsync() → LastActivityAt = 15:00 ❌
      
... (repeats forever)

Result: Session active indefinitely even if user is idle
```

---

## After (Fixed)

```
┌─────────────────────────────────────────────────────────────────┐
│                    MainViewModel                                │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │  StartSessionExpiryMonitorAsync()                        │  │
│  │                                                          │  │
│  │  Timer: Every 5 minutes                                 │  │
│  │  └─ Check: IsSessionExpiredAsync()                      │  │
│  │     └─ Compare LastActivityAt vs timeout               │  │
│  │     └─ If expired: ClearCurrentUser() + SessionExpired  │  │
│  │                                                          │  │
│  │  ✅ NO activity update in timer                         │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │  OnUserActivityDetectedAsync()                           │  │
│  │  └─ Updates LastActivityAt = DateTime.UtcNow            │  │
│  │     (Called only on real user interaction)              │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                 │
│  User Commands (RefreshDashboard, SyncData, etc.)             │
│  ├─ RefreshDashboardAsync()                                   │
│  │  └─ await OnUserActivityDetectedAsync() ✅               │
│  ├─ SyncDataAsync()                                           │
│  │  └─ await OnUserActivityDetectedAsync() ✅               │
│  ├─ SelectBusiness()                                          │
│  │  └─ await OnUserActivityDetectedAsync() ✅               │
│  └─ ... (all user-triggered commands)                        │
└─────────────────────────────────────────────────────────────────┘

Result: Session expires correctly after inactivity timeout
```

### Timeline Example (After)

```
0:00  User logs in
      LastActivityAt = 0:00
      
0:30  User clicks "Refresh Dashboard"
      OnUserActivityDetectedAsync() → LastActivityAt = 0:30 ✅
      
5:00  Timer tick
      IsSessionExpiredAsync() → Not expired (4:30 < 30 min timeout)
      
5:15  User clicks "Sync Data"
      OnUserActivityDetectedAsync() → LastActivityAt = 5:15 ✅
      
10:00 Timer tick
      IsSessionExpiredAsync() → Not expired (4:45 < 30 min timeout)
      
30:00 Timer tick
      IsSessionExpiredAsync() → EXPIRED (30 min >= 30 min timeout) ✅
      ClearCurrentUser()
      SessionExpired event fired
      
Result: Session expires after 30 minutes of inactivity
```

---

## Data Flow Comparison

### Before (Broken)
```
Timer Loop (5 min)
    ↓
IsSessionExpiredAsync()
    ↓
UpdateActivityAsync() ← ALWAYS CALLED
    ↓
LastActivityAt = Now ← RESET EVERY 5 MIN
    ↓
Session never expires ❌
```

### After (Fixed)
```
User Action (Click, Navigate, etc.)
    ↓
Command Executed (RefreshDashboard, SyncData, etc.)
    ↓
OnUserActivityDetectedAsync()
    ↓
UpdateActivityAsync()
    ↓
LastActivityAt = Now ← ONLY ON REAL ACTIVITY ✅

---

Timer Loop (5 min)
    ↓
IsSessionExpiredAsync()
    ↓
Compare LastActivityAt vs Timeout
    ↓
If expired: ClearCurrentUser() ✅
```

---

## Session Lifecycle

### Cashier Role (30-minute timeout)

```
Timeline:
0:00   Login
       LastActivityAt = 0:00
       
0:15   User processes sale
       OnUserActivityDetectedAsync() called
       LastActivityAt = 0:15
       
5:00   Timer checks expiry
       Elapsed = 4:45 < 30 min → Not expired
       
10:00  User navigates to reports
       OnUserActivityDetectedAsync() called
       LastActivityAt = 10:00
       
15:00  Timer checks expiry
       Elapsed = 5:00 < 30 min → Not expired
       
40:00  Timer checks expiry
       Elapsed = 30:00 >= 30 min → EXPIRED ✅
       Session cleared
       User redirected to login
```

### Business Owner Role (120-minute timeout)

```
Timeline:
0:00   Login
       LastActivityAt = 0:00
       
30:00  User processes sale
       OnUserActivityDetectedAsync() called
       LastActivityAt = 30:00
       
35:00  Timer checks expiry
       Elapsed = 5:00 < 120 min → Not expired
       
150:00 Timer checks expiry
       Elapsed = 120:00 >= 120 min → EXPIRED ✅
       Session cleared
       User redirected to login
```

---

## Integration Points

### Commands That Update Activity
- `LoadBusinessesAsync()` - Navigation
- `LoadShopsForBusinessAsync()` - Navigation
- `RefreshDashboardAsync()` - User action
- `SyncDataAsync()` - User action
- `SelectBusiness()` - Selection
- `SelectShop()` - Selection

### Future Integration Points
- SaleViewModel commands (ProcessSale, etc.)
- InventoryViewModel commands (UpdateStock, etc.)
- ReportsViewModel commands (GenerateReport, etc.)
- Any other user-triggered operations

### Pattern for New ViewModels
```csharp
[RelayCommand]
private async Task MyUserActionAsync()
{
    // 1. Update session activity
    await OnUserActivityDetectedAsync();
    
    // 2. Perform the actual action
    // ... your business logic
}
```

---

## Security Implications

### Before (Vulnerable)
- ❌ Sessions never expire
- ❌ Abandoned terminals remain logged in indefinitely
- ❌ Unauthorized access possible
- ❌ Compliance violations (PCI-DSS, etc.)

### After (Secure)
- ✅ Sessions expire after configured inactivity
- ✅ Abandoned terminals auto-logout
- ✅ Role-based timeout enforcement
- ✅ Compliance with security standards
- ✅ Audit trail of session activity

---

## Performance Impact

### Before
- Timer runs every 5 minutes
- Each tick: 1 DB query (IsSessionExpiredAsync) + 1 DB write (UpdateActivityAsync)
- Total: 2 DB operations per 5 minutes

### After
- Timer runs every 5 minutes
- Each tick: 1 DB query (IsSessionExpiredAsync) only
- Activity updates: Only on user action (variable frequency)
- Total: Fewer DB operations overall (no unnecessary writes)

**Result:** Better performance + better security ✅
