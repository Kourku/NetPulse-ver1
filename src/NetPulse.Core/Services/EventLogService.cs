using System;
using System.Collections.Generic;
using System.Linq;
using NetPulse.Core.Models;

namespace NetPulse.Core.Services;

public class EventLogService
{
    private readonly List<ConnectionEvent> _events = new();
    private readonly object _lock = new();

    public event EventHandler<ConnectionEvent>? EventLogged;

    public void LogEvent(string eventType, string severity, string description, string targetName = "", double? metricValue = null, string? contextData = null)
    {
        var evt = new ConnectionEvent
        {
            Timestamp = DateTime.UtcNow,
            EventType = eventType,
            Severity = severity,
            Description = description,
            TargetName = targetName,
            MetricValue = metricValue,
            ContextData = contextData
        };

        lock (_lock)
        {
            _events.Insert(0, evt);
            if (_events.Count > 1000)
            {
                _events.RemoveAt(_events.Count - 1);
            }
        }

        EventLogged?.Invoke(this, evt);
    }

    public IReadOnlyList<ConnectionEvent> GetRecentEvents(int maxCount = 100, string? filterSeverity = null)
    {
        lock (_lock)
        {
            var query = _events.AsEnumerable();
            if (!string.IsNullOrEmpty(filterSeverity) && !filterSeverity.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(e => e.Severity.Equals(filterSeverity, StringComparison.OrdinalIgnoreCase));
            }
            return query.Take(maxCount).ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _events.Clear();
        }
    }
}
