# BCSessionSync

A simple console application that synchronizes Beyond Compare session settings across multiple sessions in the same sync group.

## Overview

This tool monitors your `BCSessions.xml` file and automatically synchronizes session properties (Filters, Rules, State) between all sessions that share a common sync keyword in their name.

## Installation

1. Navigate to the project directory:
   ```powershell
   cd C:\Users\kodyb\WasatchWizard\BCSessionSync
   ```

2. Restore dependencies and build:
   ```powershell
   dotnet restore
   dotnet build
   ```

3. Run manually (one-time sync):
   ```powershell
   dotnet run --no-build
   ```

4. Or publish for standalone use:
   ```powershell
   dotnet publish -c Release -r win-x64 --self-contained true -o ./publish/win-x64
   ```

## Usage

### One-Time Sync (Default)

Run the app to perform a single sync operation and exit:

```powershell
cd C:\Users\kodyb\WasatchWizard\BCSessionSync
dotnet run --no-build
```

The app will:
1. Load settings from `backup-settings.yaml`
2. Scan all sessions in BCSessions.xml
3. Synchronize groups where timestamps differ
4. Exit automatically

### Continuous Monitoring Mode

Run the app to monitor BCSessions.xml for changes and sync automatically:

```powershell
cd C:\Users\kodyb\WasatchWizard\BCSessionSync
dotnet run --no-build --monitor
```

The app will:
1. Start monitoring `BCSessions.xml` in real-time
2. Sync all groups whenever a change is detected (after 3-second debounce)
3. Handle file locks gracefully (retries if Beyond Compare has the file open)
4. Wait for you to press Enter or Ctrl+C to exit

## Configuration

Edit `backup-settings.yaml` to define your sync groups:

```yaml
sync_groups:
  - keyword: '[~]'
    name: 'User Profile Sync'
    
  - keyword: '[scoop]'
    name: 'Scoop Sync'
    
  - keyword: '[comp]'
    name: 'Computer Sync'

bc_sessions_file: 'C:\Users\kodyb\AppData\Roaming\Scooter Software\Beyond Compare 5\BCSessions.xml'
```

### Session Naming Convention

Sessions should include the sync keyword anywhere in their name. Examples:

- `1 Personal :: [~] (personal_1t)`
- `2 Nomad :: [scoop] nomad_4tb`
- `[~] 3 Hermit :: [comp] hermit_8tb`

All sessions containing the same keyword will be synchronized together.

## How It Works

### Sync Logic

For each sync group:
1. If only 1 session exists → Skip (nothing to sync with)
2. If all timestamps match → Skip (no changes detected)
3. If timestamps differ → Use newest as source, update all others

Properties synchronized:
- `LastModified` timestamp
- `Filters` element (includes all exclusion patterns)
- `Rules` element
- `State` element

### Real-Time Monitoring

When monitoring mode is enabled (`--monitor`):
1. FileSystemWatcher monitors BCSessions.xml for changes
2. On change detected → debounce timer starts (3 seconds)
3. After 3 seconds of no activity:
   - If file locked by Beyond Compare → wait 2 more seconds and retry
   - If unlocked → perform sync automatically

## Logging

All operations are logged to `logs/sync_YYYYMMDD.log`:

```
[2026-03-02 17:45:23.456] [INFO] Started monitoring: C:\Users\kodyb\AppData\Roaming\Scooter Software\Beyond Compare 5\BCSessions.xml
[2026-03-02 17:45:23.456] [INFO] Synced: 'Personal 1TB (userprofile_sync) (personal_1t)' <- 'Nomad (userprofile_sync) (nomad_4tb)'
```

## Troubleshooting

### "BCSessions.xml not found"
- Ensure the path in `backup-settings.yaml` is correct
- Check that Beyond Compare 5 is installed and has been run at least once

### "Need at least 2 sessions to sync"
- Create more sessions with the same sync keyword in their name
- At least 2 sessions are required for synchronization

### "All timestamps match. No sync needed."
- This is normal when no changes have been made since last sync
- The app correctly detected that nothing needs updating

### File Locked Error (in monitoring mode)
- Beyond Compare may have BCSessions.xml open while running
- App will automatically retry after 2 seconds
- If file remains locked for extended period, check if Beyond Compare is still editing sessions

## Future Enhancements (v2)

- Destination-specific exclusion management via `#EXCLUDED-*` prefixes
- System tray icon for quick access
- Dry-run mode to preview changes before applying
- Automatic creation of missing destination sessions

## License

This tool is provided as-is for personal use. Beyond Compare is a commercial product by Scooter Software.
