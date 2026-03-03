namespace BCSessionSync;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using YamlDotNet.Serialization;

class Program
{
  private static readonly string _settingsFile = "backup-settings.yaml";
  private static readonly Lock _logLock = new();

  private static string? _bcSessionsFile;
  private static List<SyncGroup> _syncGroups = [];

  // FileSystemWatcher and timer for monitoring
  private static FileSystemWatcher? _fileWatcher;
  private static System.Timers.Timer? _debounceTimer;

  // Prevent triggering the file watcher from our own writes
  private static bool _monitoringIsPaused = false;

  static async Task Main( string[] args )
  {
    Console.WriteLine("========================================");
    Console.WriteLine("  BCSessionSync - sourceSession Synchronizer");
    Console.WriteLine("========================================\n");

    try {
      // Load settings
      if (!await LoadSettingsAsync()) {
        Console.WriteLine("Failed to load settings. Exiting.");
        return;
      }

      // Check if BCSessions.xml exists
      if (!File.Exists(_bcSessionsFile!)) {
        Console.WriteLine($"Error: BCSessions.xml not found at {_bcSessionsFile}");
        Console.WriteLine("Please ensure Beyond Compare is installed and sessions exist.");
        return;
      }

      // Check for --monitor flag to enable background monitoring
      var monitorMode = args.Contains("--monitor", StringComparer.OrdinalIgnoreCase);

      if (monitorMode) {
        StartBackgroundMonitoring();

        Console.WriteLine("\n========================================");
        Console.WriteLine("  Monitoring BCSessions.xml for changes...");
        Console.WriteLine("  Press Enter to exit, or Ctrl+C");
        Console.WriteLine("========================================\n");

        // Wait for user input (keeps app running)
        while (!Console.KeyAvailable) {
          await Task.Delay(100);
        }

        var key = Console.ReadKey(intercept: true).Key;
        if (key == ConsoleKey.Enter || key == ConsoleKey.C) {
          StopMonitoring();
          Console.WriteLine("\nStopping monitoring...");
          LogInfo("User stopped the application");
        }
      }

      // Run initial sync (always runs, regardless of monitor mode)
      await SyncAllGroupsAsync();

      if (!monitorMode) {
        Console.WriteLine("\n========================================");
        Console.WriteLine("  Sync complete. Press any key to exit...");
        Console.WriteLine("========================================");
        Console.ReadKey();
      }
    } catch (Exception ex) {
      LogError($"Fatal error: {ex.Message}");
      Console.WriteLine($"\nError: {ex.Message}");
      Console.WriteLine("\nPress any key to exit...");
      Console.ReadKey();
    }
  }

  static async Task<bool> LoadSettingsAsync()
  {
    try {
      if (!File.Exists(_settingsFile)) {
        LogError($"Settings file not found: {_settingsFile}");
        return false;
      }

      var yaml = await File.ReadAllTextAsync(_settingsFile);
      var deserializer = new DeserializerBuilder().Build();
      var settings = deserializer.Deserialize<Settings>(yaml);

      _syncGroups = new List<SyncGroup>(settings.SyncGroups.Count);
      foreach (var group in settings.SyncGroups) {
        if (string.IsNullOrWhiteSpace(group.Keyword)) {
          LogWarning($"Skipping sync group with empty keyword: '{group.Name}'");
          continue;
        }
        _syncGroups.Add(new SyncGroup {
          Name = ExpandEnvars(group.Name),
          Keyword = ExpandEnvars(group.Keyword)
        });
      }

      _bcSessionsFile = ExpandEnvars(settings.BCSessionsFile);

      Console.WriteLine($"Loaded {_syncGroups.Count} sync groups:");
      foreach (var group in _syncGroups) {
        Console.WriteLine($"  - {group.Name} (keyword: '{group.Keyword}')");
      }
      Console.WriteLine($"\nBCSessions file: {_bcSessionsFile}\n");

      return true;
    } catch (Exception ex) {
      LogError($"Failed to load settings: {ex.Message}");
      return false;
    }
  }

  private static readonly Dictionary<string, string> _envarCache = Environment.GetEnvironmentVariables()
                                                                              .Cast<System.Collections.DictionaryEntry>()
                                                                              .ToDictionary(k => (string)k.Key!, v => (string)v.Value!);

  static string ExpandEnvars( string text )
  {
    if (string.IsNullOrWhiteSpace(text)) {
      return text;
    }

    while (true) {
      if (text.Contains('%')) {
        // Support '%VAR%' syntax
        foreach (var (key, value) in _envarCache) {
          var placeholder = $"%{key}%";
          if (text.Contains(placeholder, StringComparison.OrdinalIgnoreCase)) {
            text = text.Replace(placeholder, value, StringComparison.OrdinalIgnoreCase);
            continue;
          }
        }
        // If we found a '%' but couldn't replace it, it means the variable is missing?
        if (text.Contains('%')) {
          var start = text.IndexOf('%');
          var end = text.IndexOf('%', start + 1);
          if (start >= 0 && end > start) {
            var key = text.Substring(start + 1, end - start - 1);
            LogException(new Exception($"Environment variable '%{key}%' specified in '{text}', does not exist."));
          }
        }
      }
      if (text.Contains("$(")) {
        // Support '$(VAR)' syntax
        foreach (var kvp in _envarCache) {
          var placeholder = $"$({kvp.Key})";
          if (text.Contains(placeholder, StringComparison.OrdinalIgnoreCase)) {
            text = text.Replace(placeholder, kvp.Value, StringComparison.OrdinalIgnoreCase);
            continue;
          }
        }
        // If we found a '$(' but couldn't replace it, it means the variable is missing?
        if (text.Contains("$(")) {
          var start = text.IndexOf("$(");
          var end = text.IndexOf(')', start + 2);
          if (start >= 0 && end > start) {
            var key = text.Substring(start + 2, end - start - 2);
            LogException(new Exception($"Environment variable '$({key})' specified in '{text}', does not exist."));
          }
        }
      }
      if (text.Contains("${env:")) {
        // Support '${env:VAR}' syntax
        foreach (var kvp in _envarCache) {
          var placeholder = $"${{env:{kvp.Key}}}";
          if (text.Contains(placeholder, StringComparison.OrdinalIgnoreCase)) {
            text = text.Replace(placeholder, kvp.Value, StringComparison.OrdinalIgnoreCase);
            continue;
          }
        }
        // If we found a '${env:' but couldn't replace it, it means the variable is missing?
        if (text.Contains("${env:")) {
          var start = text.IndexOf("${env:");
          var end = text.IndexOf('}', start + 6);
          if (start >= 0 && end > start) {
            var key = text.Substring(start + 6, end - start - 6);
            LogException(new Exception($"Environment variable '${{env:{key}}}' specified in '{text}', does not exist."));
          }
        }
      }
      if (text.Contains("${")) {
        // Support '${VAR}' syntax
        foreach (var kvp in _envarCache) {
          var placeholder = $"${{{kvp.Key}}}";
          if (text.Contains(placeholder, StringComparison.OrdinalIgnoreCase)) {
            text = text.Replace(placeholder, kvp.Value, StringComparison.OrdinalIgnoreCase);
            continue;
          }
        }
        // If we found a '${' but couldn't replace it, it means the variable is missing?
        if (text.Contains("${")) {
          var start = text.IndexOf("${");
          var end = text.IndexOf('}', start + 2);
          if (start >= 0 && end > start) {
            var key = text.Substring(start + 2, end - start - 2);
            LogException(new Exception($"Environment variable '${{{key}}}' specified in '{text}', does not exist."));
          }
        }
      }
      break; // Only reached if something was NOT replaced!
    }

    return text;
  }

  static void StartBackgroundMonitoring()
  {
    try {
      var dir = Path.GetDirectoryName(_bcSessionsFile!)!;
      var fileName = Path.GetFileName(_bcSessionsFile!);

      _fileWatcher = new FileSystemWatcher(dir, fileName) {
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
        EnableRaisingEvents = true
      };

      _monitoringIsPaused = false;

      _fileWatcher.Changed += OnBcSessionsChanged;
      _fileWatcher.Error += OnFileWatcherError;

      // Create debounce timer (wait 3 seconds after change before syncing)
      _debounceTimer = new System.Timers.Timer(3000);
      _debounceTimer.Elapsed += OnDebounceElapsed;
      _debounceTimer.AutoReset = false;

      LogInfo($"Started monitoring: {_bcSessionsFile}");
    } catch (Exception ex) {
      LogError($"Failed to start file watcher: {ex.Message}");
    }
  }

  static void StopMonitoring()
  {
    try {
      _debounceTimer?.Stop();
      _fileWatcher?.Dispose();
      _debounceTimer?.Dispose();

      _fileWatcher = null;
      _debounceTimer = null;
    } catch (Exception ex) {
      LogError($"Failed to stop file watcher: {ex.Message}");
    }
  }

  static void OnBcSessionsChanged( object sender, FileSystemEventArgs e )
  {
    if (_monitoringIsPaused) {
      return;
    }

    // Debounce rapid changes - only sync after 3 seconds of no activity
    _debounceTimer?.Start();
    LogInfo($"BCSessions.xml changed. Waiting for changes to settle...");
  }

  static async void OnDebounceElapsed( object? sender, System.Timers.ElapsedEventArgs e )
  {
    if (_monitoringIsPaused) {
      return;
    }

    // Check if file is locked by Beyond Compare
    if (IsFileLocked(_bcSessionsFile!)) {
      LogWarning("BCSessions.xml is currently locked. Retrying after 2 seconds...");
      await Task.Delay(2000);

      if (!IsFileLocked(_bcSessionsFile!)) {
        _ = SyncAllGroupsAsync(); // Fire and forget - errors handled in method
      } else {
        LogError("BCSessions.xml is still locked. Skipping sync.");
      }
    } else {
      await SyncAllGroupsAsync();
    }
  }

  static void OnFileWatcherError( object? sender, ErrorEventArgs e )
  {
    if (e.GetException() != null) {
      LogError($"File watcher error: {e.GetException().Message}");
    }
  }

  static bool IsFileLocked( string path )
  {
    try {
      using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
      return false; // Not locked
    } catch (IOException) {
      return true; // Locked by another process
    }
  }

  static async Task SyncAllGroupsAsync()
  {
    var xmlContent = await File.ReadAllTextAsync(_bcSessionsFile!);
    var doc = new XmlDocument();
    doc.LoadXml(xmlContent);

    // Find all TDirCompareSession elements recursively
    var allSessions = FindAllSessions(doc.DocumentElement!);

    Console.WriteLine($"Found {allSessions.Count} total sessions in BCSessions.xml\n");

    foreach (var group in _syncGroups) {
      await SyncGroupAsync(group, allSessions, doc);
    }

    // Save the modified XML back to file
    var sb = new StringBuilder();
    using (var writer = XmlWriter.Create(sb, new XmlWriterSettings {
      Indent = true,
      Encoding = Encoding.UTF8,
      OmitXmlDeclaration = false
    })) {
      doc.Save(writer);
    }

    PauseMonitoring();

    await File.WriteAllTextAsync(_bcSessionsFile!, sb.ToString());
    LogInfo("BCSessions.xml saved successfully");

    UnPauseMonitoring();
  }

  private static void PauseMonitoring()
  {
    _monitoringIsPaused = true;
  }

  private static void UnPauseMonitoring()
  {
    _monitoringIsPaused = false;
  }

  static List<SessionInfo> FindAllSessions( XmlNode? node )
  {
    var sessions = new List<SessionInfo>();

    if (node == null) {
      return sessions;
    }

    // Check if this is a TDirCompareSession element
    if (node.Name == "TDirCompareSession") {
      var nameAttr = node.Attributes?["Value"];
      if (nameAttr != null) {
        var lastModifiedNode = node.SelectSingleNode("LastModified");
        string? lastModified = null;

        if (lastModifiedNode?.Attributes?["Value"] != null) {
          lastModified = lastModifiedNode.Attributes["Value"].Value;
        }

        sessions.Add(new SessionInfo {
          Name = nameAttr.Value,
          LastModified = lastModified,
          XmlNode = node
        });
      }
    }

    // Recursively search child nodes
    foreach (XmlNode child in node.ChildNodes) {
      sessions.AddRange(FindAllSessions(child));
    }

    return sessions;
  }

  static async Task SyncGroupAsync( SyncGroup group, List<SessionInfo> allSessions, XmlDocument doc )
  {
    Console.WriteLine($"Processing group: '{group.Name}' (keyword: '{group.Keyword}')");

    // Find all sessions that contain this keyword in their name
    var groupSessions = allSessions.Where(s =>
        s.Name != null && s.Name.Contains(group.Keyword, StringComparison.OrdinalIgnoreCase))
        .ToList();

    Console.WriteLine($"  Found {groupSessions.Count} session(s) in this group");

    if (groupSessions.Count < 2) {
      Console.WriteLine("  Skipping: Need at least 2 sessions to sync\n");
      return;
    }

    // Parse timestamps and find the newest session
    var sessionsWithTimestamps = new List<(SessionInfo Session, DateTime Timestamp)>();

    foreach (var session in groupSessions) {
      if (!string.IsNullOrEmpty(session.LastModified)) {
        if (DateTime.TryParse(session.LastModified, out var dt)) {
          sessionsWithTimestamps.Add((session, dt));
        } else {
          LogWarning($"Could not parse timestamp for session: {session.Name}");
        }
      }
    }

    if (sessionsWithTimestamps.Count == 0) {
      Console.WriteLine("  Skipping: No valid timestamps found\n");
      return;
    }

    // Check if all timestamps are the same
    var uniqueTimestamps = sessionsWithTimestamps.Select(s => s.Timestamp).Distinct().ToList();

    if (uniqueTimestamps.Count == 1) {
      Console.WriteLine("  All timestamps match. No sync needed.\n");
      return;
    }

    // Find the newest session (source of truth)
    var (sourceSession, sourceTimestamp) = sessionsWithTimestamps.OrderByDescending(s => s.Timestamp).First();

    Console.WriteLine($"  Source: '{sourceSession.Name}' (modified: {sourceTimestamp})");
    Console.WriteLine($"  Target: {sessionsWithTimestamps.Count - 1} other session(s)\n");

    // Sync all other sessions to match the source
    var syncedCount = 0;
    foreach (var (session, timestamp) in sessionsWithTimestamps) {
      if (session == sourceSession) {
        continue;
      }

      await SyncSessionAsync(sourceSession, session);
      syncedCount++;
    }

    Console.WriteLine($"  Synchronized {syncedCount} session(s)\n");
  }

  static async Task SyncSessionAsync( SessionInfo source, SessionInfo target )
  {
    // Copy LastModified from source to target
    var lastModifiedNode = target.XmlNode?.SelectSingleNode("LastModified");
    if (lastModifiedNode?.Attributes?["Value"]?.Value != null) {
      lastModifiedNode.Attributes["Value"].Value = source.LastModified;
    }

    // Copy Filters element
    await CopyElementAsync(source, target, "Filters");

    // Copy Rules element
    await CopyElementAsync(source, target, "Rules");

    // Copy State element
    await CopyElementAsync(source, target, "State");

    LogInfo($"Synced: '{target.Name}' <- '{source.Name}'");
  }

  static async Task CopyElementAsync( SessionInfo source, SessionInfo target, string elementName )
  {
    var sourceNode = source.XmlNode.SelectSingleNode(elementName);
    var targetNode = target.XmlNode.SelectSingleNode(elementName);

    if (sourceNode != null && targetNode != null) {
      // Store reference to next sibling BEFORE removing the node
      var nextSibling = targetNode.NextSibling;

      // Remove existing target node and replace with copy of source
      target.XmlNode.RemoveChild(targetNode);

      var newNode = source.XmlNode.OwnerDocument.ImportNode(sourceNode, true);

      // Insert at the same position (before original next sibling)
      if (nextSibling != null) {
        target.XmlNode.InsertBefore(newNode, nextSibling);
      } else {
        target.XmlNode.AppendChild(newNode);
      }
    }
  }

  static void LogInfo( string message )
  {
    WriteLog("INFO", message);
    Console.WriteLine($"[INFO] {message}");
  }

  static void LogWarning( string message )
  {
    WriteLog("WARNING", message);
    Console.WriteLine($"[WARNING] {message}");
  }

  static void LogError( string message )
  {
    WriteLog("ERROR", message);
    Console.WriteLine($"[ERROR] {message}");
  }

  static void LogException( Exception ex, string? message = null )
  {
    WriteLog("ERROR", message ?? ex.Message);
    Console.WriteLine($"[ERROR] {message ?? ex.Message}");
    throw new Exception(message, ex);
  }

  static void WriteLog( string level, string message )
  {
    lock (_logLock) {
      try {
        var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        if (!Directory.Exists(logDir)) {
          Directory.CreateDirectory(logDir);
        }

        var logFile = Path.Combine(logDir, $"sync_{DateTime.Now:yyyyMMdd}.log");
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var line = $"[{timestamp}] [{level}] {message}\n";

        File.AppendAllText(logFile, line);
      } catch (Exception ex) {
        Console.WriteLine($"[LOG ERROR] Failed to write log: {ex.Message}");
      }
    }
  }
}

// Settings classes for YAML deserialization
class Settings
{
  [YamlMember(Alias = "sync_groups")]
  public List<SyncGroup> SyncGroups { get; set; } = [];

  [YamlMember(Alias = "bc_sessions_file")]
  public string BCSessionsFile { get; set; } = "";

  public override string ToString() => $"Settings :: BCSessionsFile='{BCSessionsFile}', SyncGroups.Count={SyncGroups.Count}";
}

class SyncGroup
{
  [YamlMember(Alias = "keyword")]
  public string Keyword { get; set; } = "";

  [YamlMember(Alias = "name")]
  public string Name { get; set; } = "";

  public override string ToString() => $"SyncGroup :: Name='{Name}', Keyword='{Keyword}'";
}

// sourceSession information extracted from XML
class SessionInfo
{
  public string? Name { get; set; }
  public string? LastModified { get; set; }
  public XmlNode? XmlNode { get; set; }

  public override string ToString() => $"SessionInfo :: Name='{Name}', LastModified='{LastModified}'";
}
