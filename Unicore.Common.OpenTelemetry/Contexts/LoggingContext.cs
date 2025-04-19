using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Serilog.Context;

namespace Unicore.Common.OpenTelemetry.Contexts;

/// <summary>
/// Centralized context for maintaining logging properties across asynchronous boundaries
/// </summary>
public static class LoggingContext
{
    private static readonly AsyncLocal<ConcurrentDictionary<string, object>> _localProperties =
        new AsyncLocal<ConcurrentDictionary<string, object>>();

    private static ConcurrentDictionary<string, object> Properties
    {
        get
        {
            var properties = _localProperties.Value;
            if (properties == null)
            {
                properties = new ConcurrentDictionary<string, object>();
                _localProperties.Value = properties;
            }
            return properties;
        }
    }

    /// <summary>
    /// Sets a property in the logging context that will be included in all subsequent log entries
    /// </summary>
    public static void SetProperty(string key, object value)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

        Properties[key] = value;
        LogContext.PushProperty(key, value);
    }

    /// <summary>
    /// Gets a property from the logging context
    /// </summary>
    public static bool TryGetProperty<T>(string key, out T value)
    {
        if (Properties.TryGetValue(key, out var objValue) && objValue is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Sets an operation ID and pushes it to LogContext
    /// </summary>
    public static string SetOperationId(string operationName)
    {
        var operationId = $"{operationName}_{Guid.NewGuid():N}";
        SetProperty("OperationId", operationId);
        return operationId;
    }

    /// <summary>
    /// Sets multiple properties at once and pushes them to LogContext
    /// </summary>
    public static void SetProperties(IDictionary<string, object> properties)
    {
        foreach (var property in properties)
        {
            SetProperty(property.Key, property.Value);
        }
    }

    /// <summary>
    /// Pushes all current context properties to the LogContext for the current logical execution
    /// </summary>
    public static IDisposable PushToLogContext()
    {
        List<IDisposable> disposables = new List<IDisposable>();

        foreach (var property in Properties)
        {
            disposables.Add(LogContext.PushProperty(property.Key, property.Value));
        }

        return new CompositeDisposable(disposables);
    }

    /// <summary>
    /// Clears all properties from the current context
    /// </summary>
    public static void Clear()
    {
        Properties.Clear();
    }

    /// <summary>
    /// Records the start of a performance-sensitive operation with timing
    /// </summary>
    public static OperationTiming BeginTiming(string operationName)
    {
        return new OperationTiming(operationName);
    }

    /// <summary>
    /// Helper class to handle performance timing of operations
    /// </summary>
    public class OperationTiming : IDisposable
    {
        private readonly Stopwatch _stopwatch;
        private readonly string _operationName;
        private readonly Activity _activity;

        public OperationTiming(string operationName)
        {
            _operationName = operationName;
            _stopwatch = Stopwatch.StartNew();

            // Create activity for this operation if needed
            _activity = Activity.Current?.Source?.StartActivity(operationName, ActivityKind.Internal);
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            var elapsed = _stopwatch.ElapsedMilliseconds;

            SetProperty($"{_operationName}DurationMs", elapsed);

            if (_activity != null)
            {
                _activity.SetTag("duration_ms", elapsed);
                _activity.Dispose();
            }
        }
    }

    private class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _disposables;

        public CompositeDisposable(List<IDisposable> disposables)
        {
            _disposables = disposables;
        }

        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }
        }
    }
}
