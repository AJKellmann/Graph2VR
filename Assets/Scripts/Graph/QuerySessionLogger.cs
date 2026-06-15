using System;
using System.Collections.Generic;
using System.IO;
using Dweiss;
using UnityEngine;

public static class QuerySessionLogger
{
  [Serializable]
  public class QueryLogEntry
  {
    public string timestamp;
    public string label;
    public string endpoint;
    public string graph;
    public string query;
  }

  private static readonly List<QueryLogEntry> entries = new();
  private static string sessionFilePath = "";

  public static bool IsEnabled => Settings.Instance != null && Settings.Instance.queryLoggingEnabled;

  public static void SetEnabled(bool enabled)
  {
    if (Settings.Instance == null) return;

    Settings.Instance.queryLoggingEnabled = enabled;
    PlayerPrefs.SetInt("QueryLoggingEnabled", enabled ? 1 : 0);
    PlayerPrefs.Save();

    if (enabled)
    {
      Log("Query logging enabled", "# Query logging enabled");
    }
  }

  public static void Log(string label, string query)
  {
    if (!IsEnabled) return;

    QueryLogEntry entry = new()
    {
      timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
      label = label,
      endpoint = Settings.Instance.sparqlEndpoint,
      graph = Settings.Instance.baseURI,
      query = query
    };

    entries.Add(entry);
    AppendEntry(entry);
  }

  public static List<QueryLogEntry> GetEntries()
  {
    return new List<QueryLogEntry>(entries);
  }

  public static void Restore(List<QueryLogEntry> restoredEntries)
  {
    entries.Clear();
    if (restoredEntries != null)
    {
      entries.AddRange(restoredEntries);
    }
  }

  public static string Export()
  {
    if (File.Exists(sessionFilePath))
    {
      Debug.Log("Query log is available at " + sessionFilePath);
      return sessionFilePath;
    }

    EnsureSessionFile();
    if (entries.Count > 0)
    {
      File.AppendAllText(sessionFilePath, FormatEntries(entries, false));
    }

    Debug.Log("Query log is available at " + sessionFilePath);
    return sessionFilePath;
  }

  private static void AppendEntry(QueryLogEntry entry)
  {
    try
    {
      EnsureSessionFile();
      File.AppendAllText(sessionFilePath, FormatEntry(entry));
    }
    catch (Exception exception)
    {
      Debug.LogWarning("Could not write query log: " + exception.Message);
    }
  }

  private static void EnsureSessionFile()
  {
    if (!string.IsNullOrEmpty(sessionFilePath)) return;

    string fileName = "Graph2VR_QueryLog_CurrentSession_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
    sessionFilePath = Path.Combine(Application.persistentDataPath, fileName);
    File.WriteAllText(sessionFilePath, "# Graph2VR query log\n# Created " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n\n");
  }

  private static string FormatEntries(List<QueryLogEntry> logEntries, bool includeHeader = true)
  {
    string text = includeHeader ? "# Graph2VR query log\n# Exported " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n\n" : "";
    if (logEntries.Count == 0)
    {
      text += "# No query entries recorded. Query logging may have been disabled during the session.\n";
    }
    foreach (QueryLogEntry entry in logEntries)
    {
      text += FormatEntry(entry);
    }
    return text;
  }

  private static string FormatEntry(QueryLogEntry entry)
  {
    return "## " + entry.timestamp + " | " + entry.label + "\n"
      + "Endpoint: " + entry.endpoint + "\n"
      + "Graph: " + (string.IsNullOrEmpty(entry.graph) ? "No specific graph" : entry.graph) + "\n"
      + (entry.query ?? "").Trim() + "\n\n";
  }
}
