using System;
using System.Collections.Generic;
using System.Linq;

namespace SamplePlugin.Services;

public sealed class ActivityLog
{
    private readonly object sync = new();
    private readonly List<string> entries = [];
    private const int MaxEntries = 200;

    public event Action? Changed;

    public void Info(string message) => Add("INF", message);

    public void Warn(string message) => Add("WRN", message);

    public void Error(string message) => Add("ERR", message);

    public IReadOnlyList<string> Snapshot()
    {
        lock (sync)
        {
            return entries.ToList();
        }
    }

    private void Add(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} [{level}] {message}";
        lock (sync)
        {
            entries.Add(line);
            if (entries.Count > MaxEntries)
            {
                entries.RemoveRange(0, entries.Count - MaxEntries);
            }
        }

        Changed?.Invoke();
    }
}
