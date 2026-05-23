# Desktop-Server Communication Architecture

## Overview

The Desktop application communicates with the Server through an **offline-first sync pattern**. All data is written to a local SQLite database first, and changes are synced to the central server when connectivity is available.

## Communication Flow

```
Desktop App (Avalonia)
    ↓
Shared.Core Services (Business Logic)
    ↓
SyncEngine (Offline-First Orchestrator)
    ↓
SyncApiClient (HTTP Communication)
    ↓
Server (ASP.NET Core API)
```

## Server URL Configuration

### Current Configuration Points

The server URL is configured in **multiple layers**:

#### 1. **CrossPlatformConfigurationService** (Primary)
**File:** `src/Shared.Core/Services/CrossPlatformConfigurationService.cs`

```csharp
private string GetDefaultServerUrl()
{
    // This should be configurable per environment
    return "https://api.offlinepos.local";
}
```

**Current hardcoded value:** `https://api.offlinepos.local`

This service provides:
- `GetSyncConfigurationAsync()` - Returns `SyncConfiguration` with the server URL
- `UpdateSyncConfigurationAsync()` - Allows runtime updates to the configuration

#### 2. **Shared.Core DependencyInjection** (Fallback)
**File:** `src/Shared.Core/DependencyInjection/ServiceCollectionExtensions.cs`

```csharp
services.AddSingleton(provider => new SyncConfiguration
{
    DeviceId = Guid.NewGuid(),
    ServerBaseUrl = "https://api.example.com", // Fallback value
    SyncInterval = TimeSpan.FromMinutes(5),
    MaxRetryAttempts = 3,
    InitialRetryDelay = TimeSpan.FromSeconds(1),
    RetryBackoffMultiplier = 2.0
});
```

**Current fallback value:** `https://api.example.com`

#### 3. **Environment Variables** (Not Currently Used)
**File:** `.env` and `.env.example`

```env
API_BASE_URL=http://localhost:5000
```

Currently defined but **not consumed by the Desktop application**. This is used by the Server and WebDashboard.

### SyncConfiguration DTO
**File:** `src/Shared.Core/DTOs/SyncDTOs.cs`

```csharp
public class SyncConfiguration
{
    public Guid DeviceId { get; set; }
    public string ServerUrl { get; set; } = string.Empty;
    public string ServerBaseUrl { get; set; } = string.Empty;  // ← Used for sync
    public string ApiKey { get; set; } = string.Empty;
    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan ConnectivityCheckInterval { get; set; } = TimeSpan.FromMinutes(1);
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public double RetryBackoffMultiplier { get; set; } = 2.0;
    public int BatchSize { get; set; } = 100;
}
```

## How Desktop Uses the Server URL

### 1. **SyncEngine** (Orchestrator)
**File:** `src/Shared.Core/Services/SyncEngine.cs`

The `SyncEngine` uses the server URL to:
- Check server reachability via `ConnectivityService.IsServerReachableAsync(_configuration.ServerBaseUrl)`
- Trigger sync operations when the server is reachable
- Handle offline/online transitions

```csharp
var isReachable = await _connectivityService.IsServerReachableAsync(_configuration.ServerBaseUrl);
if (isReachable && !_isSyncing)
{
    // Trigger sync
}
```

### 2. **SyncApiClient** (HTTP Communication)
**File:** `src/Shared.Core/Services/SyncApiClient.cs`

The `SyncApiClient` receives the `SyncConfiguration` and uses `ServerBaseUrl` to:
- Upload sales and inventory changes
- Download product and stock updates
- Register devices
- Authenticate with the server

```csharp
public class SyncApiClient : ISyncApiClient
{
    private readonly HttpClient _httpClient;
    private readonly SyncConfiguration _configuration;

    public SyncApiClient(ILogger<SyncApiClient> logger, HttpClient httpClient, SyncConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<SyncApiResult> UploadChangesAsync(SyncUploadRequest request)
    {
        // Uses _configuration.ServerBaseUrl to construct API endpoints
        // Example: POST {ServerBaseUrl}/api/sync/upload
    }
}
```

### 3. **ConnectivityService** (Network Checks)
**File:** `src/Shared.Core/Services/ConnectivityService.cs`

Checks if the server is reachable:

```csharp
public async Task<bool> IsServerReachableAsync(string serverUrl, TimeSpan timeout = default)
{
    var uri = new Uri(serverUrl);
    var hostname = uri.Host;
    
    // Ping the hostname to check connectivity
    var reply = await new Ping().SendPingAsync(hostname, (int)timeout.TotalMilliseconds);
    return reply.Status == IPStatus.Success;
}
```

## Dependency Injection Setup

### Desktop Application Initialization
**File:** `src/Desktop/Program.cs`

```csharp
public override void OnFrameworkInitializationCompleted()
{
    var services = new ServiceCollection();
    
    // Add desktop services (which includes shared core)
    services.AddDesktopServices(connectionString);
    
    _serviceProvider = services.BuildServiceProvider();
}
```

### Desktop Services Registration
**File:** `src/Desktop/Services/ServiceCollectionExtensions.cs`

```csharp
public static IServiceCollection AddDesktopServices(this IServiceCollection services, string connectionString)
{
    // Add shared core services (includes SyncConfiguration, SyncEngine, SyncApiClient)
    services.AddSharedCore(connectionString);
    
    // Add desktop-specific services
    services.AddScoped<GlobalExceptionHandlerService>();
    
    // Add ViewModels
    services.AddTransient<MainViewModel>();
    // ... other ViewModels
    
    return services;
}
```

### Shared Core Registration
**File:** `src/Shared.Core/DependencyInjection/ServiceCollectionExtensions.cs`

```csharp
public static IServiceCollection AddSharedCore(this IServiceCollection services, string connectionString)
{
    // Register SyncConfiguration as singleton
    services.AddSingleton(provider => new SyncConfiguration
    {
        DeviceId = Guid.NewGuid(),
        ServerBaseUrl = "https://api.example.com",
        SyncInterval = TimeSpan.FromMinutes(5),
        MaxRetryAttempts = 3,
        InitialRetryDelay = TimeSpan.FromSeconds(1),
        RetryBackoffMultiplier = 2.0
    });

    // Register SyncEngine
    services.AddSingleton<ISyncEngine, SyncEngine>();

    // Register SyncApiClient with HttpClient
    services.AddHttpClient<ISyncApiClient, SyncApiClient>();

    // Register ConnectivityService
    services.AddSingleton<IConnectivityService, ConnectivityService>();

    // ... other services
}
```

## API Endpoints Used

The Desktop communicates with the Server using these endpoints:

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/sync/upload` | POST | Upload sales and inventory changes |
| `/api/sync/download` | GET | Download product and stock updates |
| `/api/sync/register-device` | POST | Register a new device |
| `/api/sync/authenticate` | POST | Authenticate device with API key |
| `/api/sync/business-metadata` | POST | Sync business and shop metadata |

## Configuration Recommendations

### For Development
Update `CrossPlatformConfigurationService.GetDefaultServerUrl()`:
```csharp
private string GetDefaultServerUrl()
{
    return "http://localhost:5000"; // Local development server
}
```

### For Production
Implement environment-based configuration:

```csharp
private string GetDefaultServerUrl()
{
    var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
    
    return environment switch
    {
        "Development" => "http://localhost:5000",
        "Staging" => "https://staging-api.offlinepos.com",
        "Production" => "https://api.offlinepos.com",
        _ => "https://api.offlinepos.local"
    };
}
```

### Using Environment Variables
To use the `.env` file configuration:

1. Install `DotNetEnv` NuGet package
2. Load environment variables in `Desktop/Program.cs`:
```csharp
DotNetEnv.Env.Load();
var apiBaseUrl = Environment.GetEnvironmentVariable("API_BASE_URL") ?? "http://localhost:5000";
```

3. Pass to configuration:
```csharp
services.AddSingleton(provider => new SyncConfiguration
{
    ServerBaseUrl = apiBaseUrl,
    // ... other properties
});
```

## Offline-First Behavior

1. **All writes go to SQLite first** - Changes are saved locally before attempting server sync
2. **Background sync engine** - `SyncEngine` periodically checks server connectivity
3. **Automatic retry** - Failed syncs are retried with exponential backoff
4. **Conflict resolution** - Server-side conflicts are handled by the sync logic
5. **Batch processing** - Changes are synced in batches (default: 100 records)

## Key Services Involved

| Service | Purpose |
|---------|---------|
| `ISyncEngine` | Orchestrates the sync process |
| `ISyncApiClient` | Handles HTTP communication with server |
| `IConnectivityService` | Checks network and server reachability |
| `ICrossPlatformConfigurationService` | Manages device and sync configuration |
| `IRepository<T>` | Local SQLite data access (offline-first) |

## Summary

- **Server URL is currently hardcoded** in `CrossPlatformConfigurationService.GetDefaultServerUrl()`
- **Current value:** `https://api.offlinepos.local`
- **Fallback value:** `https://api.example.com` (from DI registration)
- **Environment variables exist** but are not currently consumed by Desktop
- **Communication happens through `SyncApiClient`** which uses `SyncConfiguration.ServerBaseUrl`
- **Offline-first pattern** ensures all data is written locally first, then synced when connectivity is available
