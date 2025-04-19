using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;

namespace Unicore.Common.OpenTelemetry.Enrichments;

/// <summary>
/// Central registry for all telemetry enrichers
/// </summary>
public class EnricherRegistry
{
    private readonly List<ILogEventEnricher> _enrichers = new();
    private readonly IServiceProvider _serviceProvider;

    public EnricherRegistry(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Add an enricher to the registry
    /// </summary>
    public EnricherRegistry Add<T>() where T : ILogEventEnricher
    {
        var enricher = _serviceProvider.GetService<T>();
        if (enricher != null)
        {
            _enrichers.Add(enricher);
        }
        return this;
    }

    /// <summary>
    /// Add an enricher instance to the registry
    /// </summary>
    public EnricherRegistry Add(ILogEventEnricher enricher)
    {
        if (enricher != null)
        {
            _enrichers.Add(enricher);
        }
        return this;
    }

    /// <summary>
    /// Configure Serilog with all registered enrichers
    /// </summary>
    public void ConfigureSerilog(LoggerConfiguration loggerConfiguration)
    {
        foreach (var enricher in _enrichers)
        {
            loggerConfiguration.Enrich.With(enricher);
        }
    }

    /// <summary>
    /// Get all registered enrichers
    /// </summary>
    public IEnumerable<ILogEventEnricher> GetAll() => _enrichers;
}
