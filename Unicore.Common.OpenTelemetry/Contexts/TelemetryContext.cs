using System.Collections.Concurrent;
using System.Diagnostics;
using Serilog.Context;

namespace Unicore.Common.OpenTelemetry.Contexts;

/// <summary>
/// Provides a unified context system for all telemetry needs
/// </summary>
public static class TelemetryContext
{
    private static readonly AsyncLocal<ConcurrentDictionary<string, object>> _store =
        new AsyncLocal<ConcurrentDictionary<string, object>>();

    /// <summary>
    /// Gets the underlying context dictionary for the current execution context
    /// </summary>
    public static ConcurrentDictionary<string, object> Current =>
        _store.Value ??= new ConcurrentDictionary<string, object>();

    /// <summary>
    /// Sets a property in the telemetry context
    /// </summary>
    public static void Set(string key, object value)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

        Current[key] = value;

        // Also set in Serilog context
        LogContext.PushProperty(key, value);

        // Also set as Activity tag if there's an activity
        if (Activity.Current != null)
        {
            Activity.Current.SetTag(key, value);
        }
    }

    /// <summary>
    /// Sets multiple properties at once
    /// </summary>
    public static void Set(IDictionary<string, object> values)
    {
        foreach (var pair in values)
        {
            Set(pair.Key, pair.Value);
        }
    }

    /// <summary>
    /// Gets a property from the telemetry context
    /// </summary>
    public static T Get<T>(string key, T defaultValue = default)
    {
        if (Current.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }

        return defaultValue;
    }

    /// <summary>
    /// Removes a property from the telemetry context
    /// </summary>
    public static bool Remove(string key)
    {
        return Current.TryRemove(key, out _);
    }

    /// <summary>
    /// Clears all properties from the telemetry context
    /// </summary>
    public static void Clear()
    {
        Current.Clear();
    }

    /// <summary>
    /// Creates a new activity with the given name and enriches it with the current context
    /// </summary>
    public static Activity CreateActivity(string name, ActivitySource source, ActivityKind kind = ActivityKind.Internal)
    {
        var activity = source.StartActivity(name, kind);
        if (activity != null)
        {
            // Add all context properties as tags
            foreach (var pair in Current)
            {
                activity.SetTag(pair.Key, pair.Value);
            }
        }
        return activity;
    }
}
