using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using Unicore.Common.OpenTelemetry.Enrichments;

namespace Unicore.Common.OpenTelemetry.Configurations;

/// <summary>
/// Fluent builder for configuring logging
/// </summary>
public class LoggingBuilder
{
    private readonly IHostBuilder _hostBuilder;
    private readonly string _serviceName;
    private readonly string _serviceVersion;
    private readonly string _environment;

    private bool _includeConsole = false;
    private bool _includeDebug = false;
    private bool _includeFile = true;
    private bool _includeJsonFile = true;
    private bool _includeOtlp = true;
    private string _otlpEndpoint = "http://localhost:5317";
    private LogEventLevel _minimumLevel = LogEventLevel.Information;
    private readonly List<EnrichmentAction> _customEnrichments = new();
    private readonly Dictionary<string, LogEventLevel> _overrideLevels = new();

    // Hangfire specific settings
    private bool _configureHangfire = false;
    private LogEventLevel _hangfireLogLevel = LogEventLevel.Information;
    private bool _enrichHangfireEvents = true;
    private bool _separateHangfireLogFile = false;

    // Background job settings
    private bool _includeBackgroundJobsContext = true;

    // File settings
    private string _logFileDirectory = "logs";
    private int _retainedFileDays = 7;
    private int _fileSizeLimitMb = 5;

    // Environment-specific settings
    private bool _forceConsoleLogging = false;
    private bool _disableConsoleLoggingInProduction = true;
    private bool _useEnvironmentSpecificPaths = true;
    private string _rootLogDirectory = null;

    private delegate void EnrichmentAction(LoggerConfiguration loggerConfiguration);

    internal LoggingBuilder(
        IHostBuilder hostBuilder,
        string serviceName,
        string serviceVersion,
        string environment)
    {
        // Validate parameters
        _hostBuilder = hostBuilder ?? throw new ArgumentNullException(nameof(hostBuilder));

        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));
        _serviceName = serviceName;

        if (string.IsNullOrWhiteSpace(serviceVersion))
            throw new ArgumentException("Service version cannot be null or empty", nameof(serviceVersion));
        _serviceVersion = serviceVersion;

        if (string.IsNullOrWhiteSpace(environment))
            environment = "Development"; // Default environment
        _environment = environment;

        // Default console logging based on environment
        if (_environment.Equals("Development", StringComparison.OrdinalIgnoreCase) ||
            _environment.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            _includeConsole = true;
            _includeDebug = true;
        }
        else if (!_environment.Equals("Production", StringComparison.OrdinalIgnoreCase))
        {
            _includeConsole = true;
        }

        // Set default root log directory based on OS
        _rootLogDirectory = DetermineDefaultRootLogDirectory();
    }

    /// <summary>
    /// Determines the default root log directory based on operating system
    /// </summary>
    private string DetermineDefaultRootLogDirectory()
    {
        // Get platform-specific app data directory
        string appDataDir;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Windows, use ProgramData for logs
            appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(appDataDir, "Logs");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // On macOS, use /Library/Logs for system-wide logs or ~/Library/Logs for user logs
            appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(appDataDir, "Library", "Logs");
        }
        else // Linux and others
        {
            // On Linux, respect XDG_STATE_HOME if available, otherwise use /var/log
            string xdgStateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            if (!string.IsNullOrEmpty(xdgStateHome))
            {
                return Path.Combine(xdgStateHome, "logs");
            }

            // Check if /var/log is writable by current user
            try
            {
                string varLog = "/var/log";
                if (Directory.Exists(varLog) && HasWriteAccessToDirectory(varLog))
                {
                    return varLog;
                }
            }
            catch
            {
                // Ignore access check exceptions
            }

            // Fall back to home directory
            appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(appDataDir, ".local", "logs");
        }
    }

    /// <summary>
    /// Checks if the current user has write access to the specified directory
    /// </summary>
    private bool HasWriteAccessToDirectory(string directoryPath)
    {
        try
        {
            // Attempt to create a temporary file in the directory
            string testFile = Path.Combine(directoryPath, $"write_test_{Guid.NewGuid()}.tmp");
            using (FileStream fs = File.Create(testFile, 1, FileOptions.DeleteOnClose))
            {
                // File is automatically closed and deleted by the using statement
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Configure the root directory for log files
    /// </summary>
    public LoggingBuilder WithRootLogDirectory(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            return this; // Early return with no changes

        try
        {
            // Normalize path separators for the current platform
            rootDirectory = rootDirectory.Replace('\\', Path.DirectorySeparatorChar)
                                        .Replace('/', Path.DirectorySeparatorChar);

            // If rootDirectory is a relative path, make it absolute
            if (!Path.IsPathRooted(rootDirectory))
            {
                rootDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rootDirectory);
            }

            // Verify path doesn't contain invalid characters
            if (rootDirectory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                throw new ArgumentException("Root directory contains invalid characters", nameof(rootDirectory));
            }

            _rootLogDirectory = rootDirectory;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is PathTooLongException)
        {
            // Fall back to default directory if there are path-related issues
            Console.Error.WriteLine($"Invalid log directory path: {ex.Message}. Using default path instead.");
            _rootLogDirectory = DetermineDefaultRootLogDirectory();
        }
        return this;
    }

    /// <summary>
    /// Use standard log locations for the current operating system
    /// </summary>
    public LoggingBuilder UseStandardLogLocation()
    {
        _rootLogDirectory = DetermineDefaultRootLogDirectory();
        return this;
    }

    /// <summary>
    /// Configure whether to use environment-specific paths (default: true)
    /// </summary>
    public LoggingBuilder UseEnvironmentSpecificPaths(bool enable = true)
    {
        _useEnvironmentSpecificPaths = enable;
        return this;
    }

    /// <summary>
    /// Force console logging regardless of environment
    /// </summary>
    public LoggingBuilder ForceConsoleLogging(bool enable = true)
    {
        _forceConsoleLogging = enable;
        return this;
    }

    /// <summary>
    /// Configure whether to disable console logging in production (default: true)
    /// </summary>
    public LoggingBuilder DisableConsoleLoggingInProduction(bool disable = true)
    {
        _disableConsoleLoggingInProduction = disable;
        return this;
    }

    /// <summary>
    /// Enable or disable console logging
    /// </summary>
    public LoggingBuilder WithConsoleLogging(bool enable = true)
    {
        _includeConsole = enable;
        return this;
    }

    /// <summary>
    /// Enable or disable debug logging (only useful in development)
    /// </summary>
    public LoggingBuilder WithDebugLogging(bool enable = true)
    {
        _includeDebug = enable;
        return this;
    }

    /// <summary>
    /// Enable or disable file logging
    /// </summary>
    public LoggingBuilder WithFileLogging(bool enable = true)
    {
        _includeFile = enable;
        return this;
    }

    /// <summary>
    /// Enable or disable JSON file logging
    /// </summary>
    public LoggingBuilder WithJsonFileLogging(bool enable = true)
    {
        _includeJsonFile = enable;
        return this;
    }

    /// <summary>
    /// Enable or disable OpenTelemetry logging
    /// </summary>
    public LoggingBuilder WithOtlpLogging(bool enable = true, string? endpoint = null)
    {
        _includeOtlp = enable;
        if (!string.IsNullOrEmpty(endpoint))
        {
            _otlpEndpoint = endpoint;
        }
        return this;
    }

    /// <summary>
    /// Set the minimum log level for all sinks
    /// </summary>
    public LoggingBuilder WithMinimumLevel(LogEventLevel level)
    {
        _minimumLevel = level;
        return this;
    }

    /// <summary>
    /// Override log level for specific namespaces
    /// </summary>
    public LoggingBuilder WithLogLevel(string sourceContext, LogEventLevel level)
    {
        _overrideLevels[sourceContext] = level;
        return this;
    }

    /// <summary>
    /// Configure Hangfire logging settings
    /// </summary>
    /// <param name="enable">Whether to apply special Hangfire log settings</param>
    /// <param name="logLevel">The log level to use for Hangfire namespaces</param>
    /// <param name="separateLogFile">Whether to output Hangfire logs to a separate file</param>
    /// <returns>The builder instance for method chaining</returns>
    public LoggingBuilder WithHangfireLogging(bool enable = true, LogEventLevel logLevel = LogEventLevel.Information, bool separateLogFile = false)
    {
        _configureHangfire = enable;
        _hangfireLogLevel = logLevel;
        _separateHangfireLogFile = separateLogFile;

        if (enable)
        {
            // Set log levels for Hangfire namespaces
            _overrideLevels["Hangfire"] = logLevel;
            _overrideLevels["Hangfire.Server"] = logLevel;
            _overrideLevels["Hangfire.Client"] = logLevel;
            _overrideLevels["Hangfire.Worker"] = logLevel;
            _overrideLevels["Hangfire.Storage"] = logLevel;
            _overrideLevels["Hangfire.SqlServer"] = logLevel;
        }

        return this;
    }

    /// <summary>
    /// Enable or disable enrichment of Hangfire events with job context
    /// </summary>
    public LoggingBuilder EnrichHangfireEvents(bool enable = true)
    {
        _enrichHangfireEvents = enable;
        return this;
    }

    /// <summary>
    /// Configure log file settings
    /// </summary>
    public LoggingBuilder WithLogFileSettings(string directory = "logs", int retainedDays = 7, int maxFileSizeMb = 5)
    {
        _logFileDirectory = directory;
        _retainedFileDays = retainedDays;
        _fileSizeLimitMb = maxFileSizeMb;
        return this;
    }

    /// <summary>
    /// Enable or disable background job context enrichment
    /// </summary>
    public LoggingBuilder WithBackgroundJobContext(bool enable = true)
    {
        _includeBackgroundJobsContext = enable;
        return this;
    }

    /// <summary>
    /// Add a custom enricher
    /// </summary>
    public LoggingBuilder WithEnricher<TEnricher>() where TEnricher : class, ILogEventEnricher, new()
    {
        _customEnrichments.Add(config => config.Enrich.With(new TEnricher()));
        return this;
    }

    /// <summary>
    /// Add a custom property
    /// </summary>
    public LoggingBuilder WithProperty(string name, object value)
    {
        _customEnrichments.Add(config => config.Enrich.WithProperty(name, value));
        return this;
    }

    /// <summary>
    /// Configure Serilog with all the settings
    /// </summary>
    public IHostBuilder Build()
    {
        return _hostBuilder.UseSerilog((context, services, loggerConfig) =>
        {
            try
            {
                // Start with basic configuration
                loggerConfig
                    .MinimumLevel.Is(_minimumLevel)
                    .Enrich.FromLogContext()
                    .Enrich.WithMachineName()
                    .Enrich.WithProcessId()
                    .Enrich.WithThreadId()
                    .Enrich.WithEnvironmentName()
                    .Enrich.WithProperty("ServiceName", _serviceName)
                    .Enrich.WithProperty("ServiceVersion", _serviceVersion)
                    .Enrich.WithProperty("Environment", _environment);

                // Add timestamp enrichment
                loggerConfig.Enrich.WithProperty("TimestampUtc", DateTimeOffset.UtcNow);

                // Configure level overrides
                foreach (var pair in _overrideLevels)
                {
                    loggerConfig.MinimumLevel.Override(pair.Key, pair.Value);
                }

                // Read from appsettings.json if it exists
                loggerConfig.ReadFrom.Configuration(context.Configuration);

                // Add enricher registry if available
                try
                {
                    var enricherRegistry = services.GetService(typeof(EnricherRegistry)) as EnricherRegistry;
                    if (enricherRegistry != null)
                    {
                        enricherRegistry.ConfigureSerilog(loggerConfig);
                    }
                }
                catch (Exception ex)
                {
                    // Log error but continue with configuration
                    Console.Error.WriteLine($"Error configuring enricher registry: {ex.Message}");
                }

                // Apply custom enrichments
                foreach (var enrichAction in _customEnrichments)
                {
                    enrichAction(loggerConfig);
                }

                // Define output template
                var outputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] " +
                                    "[{Level:u3}] " +
                                    "[{ServiceName}] " +
                                    "[TraceId: {TraceId}] " +
                                    "[{SourceContext}] " +
                                    "{Message:lj}{NewLine}{Exception}";

                // Determine if console logging should be enabled based on environment and settings
                bool shouldEnableConsole = ShouldEnableConsoleLogging();

                // Add console logging if enabled
                if (shouldEnableConsole)
                {
                    loggerConfig.WriteTo.Console(
                        outputTemplate: outputTemplate,
                        theme: AnsiConsoleTheme.Code
                    );
                }

                // Add debug output if enabled (only works in Debug mode)
                if (_includeDebug && (context.HostingEnvironment.IsDevelopment() ||
                                     context.HostingEnvironment.EnvironmentName.Equals("Local", StringComparison.OrdinalIgnoreCase)))
                {
                    loggerConfig.WriteTo.Debug(outputTemplate: outputTemplate);
                }

                // Add file logging if enabled
                if (_includeFile)
                {
                    var logDir = GetLogDirectory();
                    var dateFormat = DateTime.UtcNow.ToString("yyyy-MM-dd");

                    loggerConfig.WriteTo.File(
                        path: Path.Combine(logDir, $"{_serviceName}-{dateFormat}.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: _retainedFileDays,
                        fileSizeLimitBytes: _fileSizeLimitMb * 1024 * 1024,
                        outputTemplate: outputTemplate
                    );
                }

                // Add separate Hangfire log file if enabled
                if (_separateHangfireLogFile && _configureHangfire)
                {
                    var logDir = GetLogDirectory();
                    var dateFormat = DateTime.UtcNow.ToString("yyyy-MM-dd");

                    // Create a custom Hangfire sink that only logs events from Hangfire namespaces
                    loggerConfig.WriteTo.Logger(lc => lc
                        .Filter.ByIncludingOnly(evt =>
                            evt.Properties.ContainsKey("SourceContext") &&
                            (evt.Properties["SourceContext"].ToString().Contains("Hangfire") ||
                             (evt.Properties.ContainsKey("HangfireJobId") && evt.Properties["HangfireJobId"] != null)))
                        .WriteTo.File(
                            path: Path.Combine(logDir, $"{_serviceName}-hangfire-{dateFormat}.log"),
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: _retainedFileDays,
                            fileSizeLimitBytes: _fileSizeLimitMb * 1024 * 1024,
                            outputTemplate: outputTemplate
                        )
                    );
                }

                // Add JSON file logging if enabled
                if (_includeJsonFile)
                {
                    var logDir = GetLogDirectory();
                    var dateFormat = DateTime.UtcNow.ToString("yyyy-MM-dd");

                    loggerConfig.WriteTo.File(
                        formatter: new Serilog.Formatting.Json.JsonFormatter(),
                        path: Path.Combine(logDir, $"{_serviceName}-json-{dateFormat}.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: _retainedFileDays,
                        fileSizeLimitBytes: _fileSizeLimitMb * 2 * 1024 * 1024, // JSON files tend to be larger
                        shared: true
                    );
                }

                // Add job-related enrichers
                if (_configureHangfire && _enrichHangfireEvents)
                {
                    try
                    {
                        var hangfireEnricher = services.GetService(typeof(HangfireEnricher)) as HangfireEnricher;
                        if (hangfireEnricher != null)
                        {
                            loggerConfig.Enrich.With(hangfireEnricher);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log error but continue with configuration
                        Console.Error.WriteLine($"Error configuring Hangfire enricher: {ex.Message}");
                    }
                }

                if (_includeBackgroundJobsContext)
                {
                    var backgroundJobEnricher = services.GetService(typeof(BackgroundJobEnricher)) as BackgroundJobEnricher;
                    if (backgroundJobEnricher != null)
                    {
                        loggerConfig.Enrich.With(backgroundJobEnricher);
                    }
                }

                // Add OpenTelemetry logging if enabled
                if (_includeOtlp)
                {
                    var configEndpoint = context.Configuration["OpenTelemetry:Endpoint"];
                    var endpoint = !string.IsNullOrEmpty(configEndpoint) ? configEndpoint : _otlpEndpoint;

                    loggerConfig.WriteTo.OpenTelemetry(options =>
                    {
                        options.Endpoint = endpoint;
                        options.Protocol = Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc;
                        options.ResourceAttributes = new Dictionary<string, object>
                        {
                            ["service.name"] = _serviceName,
                            ["service.version"] = _serviceVersion,
                            ["deployment.environment"] = _environment,
                            ["host.name"] = Environment.MachineName
                        };
                    });
                }
            }
            catch (Exception ex)
            {
                // Last resort error handling to ensure some level of logging is configured
                Console.Error.WriteLine($"Error configuring Serilog: {ex.Message}");

                // Configure minimal fallback logging
                loggerConfig
                    .MinimumLevel.Error()
                    .WriteTo.Console()
                    .WriteTo.File("logs/error.log");
            }
        });
    }

    /// <summary>
    /// Determines whether console logging should be enabled based on settings and environment
    /// </summary>
    private bool ShouldEnableConsoleLogging()
    {
        // Force console logging if explicitly enabled
        if (_forceConsoleLogging)
        {
            return true;
        }

        // Check if console logging is explicitly requested
        if (_includeConsole)
        {
            // If in production and console logging should be disabled in production
            if (_environment.Equals("Production", StringComparison.OrdinalIgnoreCase) && _disableConsoleLoggingInProduction)
            {
                return false;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the appropriate log directory based on environment settings
    /// </summary>
    private string GetLogDirectory()
    {
        // Ensure we have a root directory
        if (string.IsNullOrEmpty(_rootLogDirectory))
        {
            _rootLogDirectory = DetermineDefaultRootLogDirectory();
        }

        string baseDir;

        try
        {
            // Start with the root directory
            baseDir = _rootLogDirectory;

            // If running in Debug mode or environment is explicitly set to "Local"
            bool isDebugOrLocal = IsDebugBuild() || _environment.Equals("Local", StringComparison.OrdinalIgnoreCase);

            if (_useEnvironmentSpecificPaths)
            {
                // For Debug/Local, use [RootDir]/ServiceName
                if (isDebugOrLocal)
                {
                    baseDir = Path.Combine(_rootLogDirectory, _serviceName);
                }
                else
                {
                    // For other environments, include the environment name in the path
                    baseDir = Path.Combine(_rootLogDirectory, _environment, _serviceName);
                }
            }
            else
            {
                // If not using environment paths, just use service name subfolder
                baseDir = Path.Combine(_rootLogDirectory, _serviceName);
            }

            // Ensure directory exists
            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }
        }
        catch (Exception ex)
        {
            // Enhanced error handling with detailed logs
            Console.Error.WriteLine($"Failed to create log directory: {ex.Message}");
            Console.Error.WriteLine($"Exception type: {ex.GetType().Name}");
            Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");

            // Use a platform-appropriate temp directory with unique service name
            string tempLogName = $"{_serviceName}_{Guid.NewGuid():N}";
            string tempDir = Path.Combine(Path.GetTempPath(), "logs", tempLogName);

            try
            {
                Directory.CreateDirectory(tempDir);
                baseDir = tempDir;
            }
            catch (Exception fallbackEx)
            {
                // If even the temp directory fails, use the current directory
                Console.Error.WriteLine($"Failed to create fallback log directory: {fallbackEx.Message}");
                baseDir = AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        return baseDir;
    }

    /// <summary>
    /// Gets or creates the log directory with structure determined by environment
    /// This overrides the previously private method with the new environment-aware functionality
    /// </summary>
    private string GetOrCreateLogDirectory()
    {
        return GetLogDirectory();
    }

    /// <summary>
    /// Determines if the application is running in debug mode
    /// </summary>
    private bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }
}
