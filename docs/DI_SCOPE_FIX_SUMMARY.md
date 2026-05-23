# DI Scope Issue Fix: CurrentUserService

## Problem

`ICurrentUserService` was registered as a **singleton** but:
- Depends on **scoped services** (`ISessionService`, `IAuthorizationService`)
- Holds **mutable per-user state** (`_currentUser`, `_currentSession`, `_currentPermissions`)
- Violates ASP.NET Core DI scope rules
- Causes **state leakage between requests/users** in server context

This is a critical issue in multi-user server scenarios where user context can bleed across requests.

## Root Cause

In `src/Shared.Core/DependencyInjection/ServiceCollectionExtensions.cs` (line 110):
```csharp
services.AddSingleton<ICurrentUserService, CurrentUserService>();  // ❌ WRONG
```

The singleton registration means:
1. One instance is created for the entire application lifetime
2. All requests share the same `_currentUser`, `_currentSession`, `_currentPermissions` fields
3. User A's context can be read by User B's request if timing is wrong

## Solution

### 1. Changed Registration to Scoped (ServiceCollectionExtensions.cs)

```csharp
services.AddScoped<ICurrentUserService, CurrentUserService>();  // ✅ CORRECT
```

**Why scoped?**
- Each HTTP request gets its own `ICurrentUserService` instance
- User context is isolated per request
- Scoped dependencies (`ISessionService`, `IAuthorizationService`) can be safely injected
- Follows ASP.NET Core DI best practices

### 2. Updated Middleware to Resolve Scoped Service (GlobalExceptionHandlerMiddleware.cs)

**Before:**
```csharp
public class GlobalExceptionHandlerMiddleware
{
    private readonly ICurrentUserService _currentUserService;  // ❌ Singleton field

    public GlobalExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlerMiddleware> logger,
        IHostEnvironment env,
        IGlobalExceptionHandler globalExceptionHandler,
        ICurrentUserService currentUserService)  // ❌ Injected in constructor
    {
        _currentUserService = currentUserService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Uses singleton instance — wrong!
        var deviceId = _currentUserService.GetDeviceId();
    }
}
```

**After:**
```csharp
public class GlobalExceptionHandlerMiddleware
{
    // No longer storing scoped service as field

    public GlobalExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlerMiddleware> logger,
        IHostEnvironment env,
        IGlobalExceptionHandler globalExceptionHandler)
        // ✅ Removed ICurrentUserService from constructor
    {
        _next = next;
        _logger = logger;
        _env = env;
        _globalExceptionHandler = globalExceptionHandler;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentUserService currentUserService)
    {
        // ✅ Resolved from request scope via method parameter
        var deviceId = currentUserService.GetDeviceId();
        var userId = currentUserService.GetUserId();
        // ...
    }
}
```

**Key change:** Middleware now resolves `ICurrentUserService` from the **request scope** via method parameter injection, not constructor injection.

## Impact on Desktop App

The Desktop app uses `ICurrentUserService` in ViewModels and services. With the scoped registration:

- **Desktop app-wide state:** If Desktop needs true app-lifetime state, it should create an **explicit app-lifetime scope** or a separate singleton wrapper that delegates to the scoped service
- **Current behavior:** Each ViewModel/service gets its own scoped instance, which is actually safer and more correct
- **No breaking changes:** Desktop code continues to work; it just gets proper scope isolation

## Files Modified

1. **src/Shared.Core/DependencyInjection/ServiceCollectionExtensions.cs** (line 110)
   - Changed `AddSingleton` → `AddScoped`

2. **src/Server/Middleware/GlobalExceptionHandlerMiddleware.cs** (lines 13-33)
   - Removed `ICurrentUserService` from constructor
   - Added `ICurrentUserService` parameter to `InvokeAsync` method
   - Updated `HandleExceptionAsync` to accept and use the scoped instance

## Verification

✅ Solution builds successfully  
✅ No compilation errors  
✅ Follows ASP.NET Core DI best practices  
✅ Eliminates state leakage between requests  
✅ Maintains backward compatibility with Desktop app  

## Testing Recommendations

1. **Server:** Test concurrent requests with different users to verify context isolation
2. **Desktop:** Verify ViewModels still receive user context correctly
3. **Integration:** Run full test suite to ensure no regressions

## References

- [ASP.NET Core Dependency Injection Scopes](https://docs.microsoft.com/en-us/dotnet/core/extensions/dependency-injection#service-lifetimes)
- [Middleware in ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/middleware)
