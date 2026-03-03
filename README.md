# BCSessionSync

A simple console application that synchronizes Beyond Compare session settings across multiple sessions in the same sync group.

## Overview

This tool monitors your `BCSessions.xml` file and automatically synchronizes session properties (Filters, Rules, State) between all sessions that share a common sync keyword in their name.

## Features

- **Group-based synchronization**: Sessions are grouped by keywords found anywhere in their name
- **Timestamp-based change detection**: Only syncs when timestamps differ within a group
- **Source of truth**: Uses the most recently modified session as the source for all others
- **Preserves exclusions**: All exclusion patterns (including `#EXCLUDED-*` prefixes) are copied as-is
- **Logging**: All operations are logged to timestamped log files

## Installation

1. Build the project:
   ```bash
   dotnet build
   ```

2. Run the application:
   ```bash
   dotnet run
   ```

3. Or publish for standalone use:
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained true
   ```

## Configuration

Edit `backup-settings.yaml` to define your sync groups:

```yaml
sync_groups:
  - keyword: 'userprofile_sync'
    name: 'User Profile Sync'
    
  - keyword: 'scoop_sync'
    name: 'Scoop Sync'

bc_sessions_file: 'C:\Users\kodyb\AppData\Roaming\Scooter Software\Beyond Compare 5\BCSessions.xml'
```

## Session Naming Convention

Sessions should include the sync keyword anywhere in their name. Examples:

- `1 Personal :: (userprofile_sync) (personal_1t)`
- `2 Nomad :: userprofile_sync nomad_4tb`
- `[~] 3 Hermit :: [userprofile_sync] hermit_8tb`

All sessions containing the same keyword will be synchronized together.

## How It Works

1. **Load Settings**: Reads sync groups from `backup-settings.yaml`
2. **Parse Sessions**: Recursively finds all `TDirCompareSession` elements in BCSessions.xml
3. **Group Sessions**: Groups sessions by their sync keywords
4. **Detect Changes**: Compares `LastModified` timestamps within each group
5. **Sync if Needed**: If timestamps differ, copies properties from newest session to others
6. **Save Changes**: Writes updated XML back to BCSessions.xml

## Sync Logic

For each sync group:
- If only 1 session exists → Skip (nothing to sync with)
- If all timestamps match → Skip (no changes detected)
- If timestamps differ → Use newest as source, update all others

Properties synchronized:
- `LastModified` timestamp
- `Filters` element (includes all exclusion patterns)
- `Rules` element
- `State` element

## Logging

All operations are logged to `logs/sync_YYYYMMDD.log`:

```
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

## Future Enhancements (v2)

- Automatic creation of missing destination sessions
- Destination-specific exclusion management via `#EXCLUDED-*` prefixes
- Background monitoring with FileSystemWatcher
- System tray icon for quick access
- Dry-run mode to preview changes before applying

## License

This tool is provided as-is for personal use. Beyond Compare is a commercial product by Scooter Software.
