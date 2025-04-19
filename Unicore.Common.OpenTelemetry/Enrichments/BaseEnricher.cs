using Serilog.Core;
using Serilog.Events;

namespace Unicore.Common.OpenTelemetry.Enrichments;

/// <summary>
/// Base class for all enrichers with error handling
/// </summary>
public abstract class BaseEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        try
        {
            EnrichInternal(logEvent, propertyFactory);
        }
        catch (Exception ex)
        {
            // Don't let enricher errors propagate
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty(
                    $"{GetType().Name}Error",
                    $"{ex.GetType().Name}: {ex.Message}"
                )
            );
        }
    }

    protected abstract void EnrichInternal(LogEvent logEvent, ILogEventPropertyFactory propertyFactory);

    /// <summary>
    /// Helper method to add a property if it has a value
    /// </summary>
    protected void AddPropertyIfNotNull(LogEvent logEvent, ILogEventPropertyFactory factory,
        string propertyName, object value)
    {
        if (value != null)
        {
            logEvent.AddPropertyIfAbsent(factory.CreateProperty(propertyName, value));
        }
    }
}
