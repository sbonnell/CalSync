# Exchange Calendar Sync

A C# application that runs in a Linux Docker container to synchronize calendar items from on-premise Exchange 2019 mailboxes to Exchange Online (Microsoft 365). This is a one-way sync that excludes attachments for security purposes.

## Features

- One-way calendar synchronization from Exchange 2019 to Exchange Online
- Monitors multiple mailboxes simultaneously
- Excludes attachments for security (attachments are never synced to Exchange Online)
- **Web interface** for monitoring and control:
  - Real-time sync status dashboard
  - View recent logs with filtering
  - Manual sync trigger
  - Per-mailbox statistics
- Runs in a Docker container on Linux
- Configurable sync intervals
- Comprehensive logging
- Automatic retry on errors
- Prevents duplicate entries using extended properties

## Prerequisites

### Exchange 2019 (On-Premise)
- Exchange Web Services (EWS) enabled
- Service account with impersonation rights for monitored mailboxes
- Network connectivity from Docker host to Exchange server

### Exchange Online (Microsoft 365)
- Azure AD App Registration with the following permissions:
  - `Calendars.ReadWrite` (Application permission)
  - Admin consent granted
- Client ID, Client Secret, and Tenant ID

### Development/Runtime
- Docker and Docker Compose
- .NET 8.0 SDK (for local development)

## Setup Instructions

### 1. Azure AD App Registration

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Azure Active Directory** > **App registrations** > **New registration**
3. Name: `Exchange Calendar Sync`
4. Register the application
5. Note the **Application (client) ID** and **Directory (tenant) ID**
6. Go to **Certificates & secrets** > **New client secret**
7. Create a secret and note its value
8. Go to **API permissions**:
   - Add permission > Microsoft Graph > Application permissions
   - Add `Calendars.ReadWrite`
   - Click **Grant admin consent**

### 2. Exchange 2019 Service Account

Create a service account with impersonation rights:

```powershell
# In Exchange Management Shell
New-ManagementRoleAssignment -Name "ImpersonationAssignment" `
    -Role ApplicationImpersonation `
    -User "DOMAIN\ServiceAccount"
```

### 3. Configuration

Edit `appsettings.json`:

```json
{
  "ExchangeOnPremise": {
    "ServerUrl": "https://exchange2019.yourdomain.com/EWS/Exchange.asmx",
    "Username": "serviceaccount",
    "Password": "your-password",
    "Domain": "YOURDOMAIN",
    "MailboxesToMonitor": [
      "user1@yourdomain.com",
      "user2@yourdomain.com"
    ]
  },
  "ExchangeOnline": {
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret"
  },
  "Sync": {
    "SyncIntervalMinutes": 5,
    "LookbackDays": 30
  }
}
```

## Running the Application

### Using Docker Compose (Recommended)

```bash
# Build and start the container
docker-compose up -d

# View logs
docker-compose logs -f

# Stop the container
docker-compose down
```

Once running, access the **web interface** at:
- **http://localhost:5000**

The web interface provides:
- Real-time sync status and statistics
- Recent logs with level filtering
- Manual sync trigger button
- Per-mailbox sync details

### Using Docker

```bash
# Build the image
docker build -t exchange-calendar-sync .

# Run the container
docker run -d \
  --name exchange-calendar-sync \
  -p 5000:5000 \
  -v $(pwd)/appsettings.json:/app/appsettings.json:ro \
  --restart unless-stopped \
  exchange-calendar-sync

# View logs
docker logs -f exchange-calendar-sync
```

Access the web interface at **http://localhost:5000**

### Local Development

```bash
# Restore dependencies
dotnet restore

# Run the application
dotnet run

# Build for release
dotnet publish -c Release -o ./publish
```

When running locally, the web interface will be available at **http://localhost:5000**

## How It Works

1. **Initialization**: Connects to both Exchange 2019 (via EWS) and Exchange Online (via Microsoft Graph API)

2. **Monitoring**: Polls each configured mailbox at the specified interval

3. **Synchronization**:
   - Retrieves calendar items from Exchange 2019
   - Filters out attachments (not included in sync)
   - Checks if items already exist in Exchange Online (using extended properties)
   - Creates or updates events in Exchange Online

4. **Tracking**: Stores the source Exchange ID in extended properties to:
   - Prevent duplicates
   - Enable updates to existing events

## Security Considerations

- Attachments are **never** synced to Exchange Online
- Credentials are stored in `appsettings.json` - consider using environment variables or secrets management in production
- SSL certificate validation is bypassed for Exchange 2019 (update `ExchangeOnPremiseService.cs:28` for production)
- Service runs with minimal permissions (no delete rights)

## Logging

Logs are written to the console and captured by Docker. Log levels can be configured in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

## Troubleshooting

### Connection Issues

**Exchange 2019:**
- Verify EWS URL is accessible from Docker container
- Check firewall rules
- Verify credentials and impersonation rights

**Exchange Online:**
- Verify Azure AD app permissions are granted
- Check Client ID, Client Secret, and Tenant ID
- Ensure mailboxes exist in Exchange Online

### Sync Issues

**Items not syncing:**
- Check logs for errors: `docker-compose logs -f`
- Verify mailbox addresses are correct
- Ensure calendar items are within the lookback/lookahead window

**Duplicate items:**
- The app uses extended properties to track synced items
- If duplicates occur, there may be an issue with extended property storage

### Performance

- Adjust `SyncIntervalMinutes` based on your needs
- Reduce `LookbackDays` to decrease initial sync time
- Add delays in `CalendarSyncService.cs:88` if hitting rate limits

## Architecture

```
┌─────────────────────────────────────────────────────┐
│              Exchange Calendar Sync                  │
│                 (Docker Container)                   │
│                                                      │
│  ┌────────────────────────────────────────────┐    │
│  │           Program.cs (Main)                 │    │
│  │  - Configuration validation                 │    │
│  │  - Service initialization                   │    │
│  │  - Sync loop orchestration                  │    │
│  └────────────────────────────────────────────┘    │
│                       │                              │
│                       ▼                              │
│  ┌────────────────────────────────────────────┐    │
│  │      CalendarSyncService                    │    │
│  │  - Coordinates sync operations              │    │
│  │  - Manages sync timing                      │    │
│  │  - Tracks last sync times                   │    │
│  └────────────────────────────────────────────┘    │
│           │                           │              │
│           ▼                           ▼              │
│  ┌──────────────────┐      ┌──────────────────┐   │
│  │ ExchangeOnPremise│      │ ExchangeOnline   │   │
│  │     Service      │      │    Service       │   │
│  │  - EWS client    │      │  - Graph client  │   │
│  │  - Calendar read │      │  - Calendar CRUD │   │
│  └──────────────────┘      └──────────────────┘   │
└─────────────────────────────────────────────────────┘
         │                              │
         ▼                              ▼
┌──────────────────┐         ┌──────────────────┐
│  Exchange 2019   │         │ Exchange Online  │
│  (On-Premise)    │         │  (Microsoft 365) │
└──────────────────┘         └──────────────────┘
```

## Web Interface

The application includes a built-in web dashboard accessible at **http://localhost:5000** that provides:

### Features
- **Status Dashboard**: Real-time sync status, total items synced, errors, and last sync time
- **Manual Sync**: Trigger an immediate sync without waiting for the scheduled interval
- **Mailbox Details**: Per-mailbox sync statistics including items synced, errors, and last sync time
- **Live Logs**: View recent logs with filtering by level (Information, Warning, Error, Debug)
- **Auto-refresh**: Dashboard and logs refresh automatically every 5 seconds

### API Endpoints

The web interface uses these REST API endpoints:

- `GET /api/sync/status` - Get current sync status and statistics
- `POST /api/sync/start` - Trigger a manual sync
- `GET /api/logs?level={level}&limit={limit}` - Retrieve logs with optional filtering

## Project Structure

```
ExchangeCalendarSync/
├── Controllers/
│   ├── SyncController.cs       # API endpoints for sync control
│   └── LogsController.cs       # API endpoints for logs
├── Logging/
│   └── InMemoryLoggerProvider.cs  # In-memory log storage
├── Models/
│   ├── AppSettings.cs          # Configuration models
│   ├── CalendarItem.cs         # Calendar item DTO
│   └── SyncStatus.cs           # Sync status models
├── Services/
│   ├── ExchangeOnPremiseService.cs  # EWS client
│   ├── ExchangeOnlineService.cs     # Graph API client
│   ├── CalendarSyncService.cs       # Sync orchestration
│   ├── SyncStatusService.cs         # Status tracking
│   └── SyncBackgroundService.cs     # Background sync service
├── wwwroot/
│   └── index.html              # Web dashboard UI
├── Program.cs                   # Application entry point
├── ExchangeCalendarSync.csproj # Project file
├── appsettings.json            # Configuration
├── Dockerfile                  # Docker image definition
├── docker-compose.yml          # Docker Compose configuration
├── .dockerignore               # Docker ignore patterns
├── .gitignore                  # Git ignore patterns
└── README.md                   # This file
```

## License

This project is provided as-is for use in synchronizing Exchange calendars.

## Support

For issues or questions:
1. Check the logs: `docker-compose logs -f`
2. Verify configuration in `appsettings.json`
3. Review the troubleshooting section above
