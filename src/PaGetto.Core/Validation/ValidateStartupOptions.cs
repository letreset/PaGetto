using System;
using PaGetto.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaGetto.Core.Validation;

/// <summary>
/// Validates PaGetto's options, used at startup.
/// </summary>
public partial class ValidateStartupOptions
{
    private readonly IOptions<PaGettoOptions> _root;
    private readonly IOptions<DatabaseOptions> _database;
    private readonly IOptions<StorageOptions> _storage;
    private readonly IOptions<MirrorOptions> _mirror;
    private readonly IOptions<HealthCheckOptions> _healthCheck;
    private readonly IOptions<StatisticsOptions> _statistics;
    private readonly ILogger<ValidateStartupOptions> _logger;

    public ValidateStartupOptions(
        IOptions<PaGettoOptions> root,
        IOptions<DatabaseOptions> database,
        IOptions<StorageOptions> storage,
        IOptions<MirrorOptions> mirror,
        IOptions<HealthCheckOptions> healthCheck,
        IOptions<StatisticsOptions> statistics,
        ILogger<ValidateStartupOptions> logger)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        _healthCheck = healthCheck ?? throw new ArgumentNullException(nameof(healthCheck));
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool Validate()
    {
        try
        {
            // Access each option to force validations to run.
            // Invalid options will trigger an "OptionsValidationException" exception.
            _ = _root.Value;
            _ = _database.Value;
            _ = _storage.Value;
            _ = _mirror.Value;
            _ = _healthCheck.Value;
            _ = _statistics.Value;

            return true;
        }
        catch (OptionsValidationException e)
        {
            foreach (var failure in e.Failures)
            {
                LogOptionsFailure(failure);
            }

            LogConfigurationInvalid(e);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{OptionsFailure}")]
    private partial void LogOptionsFailure(string optionsFailure);

    [LoggerMessage(Level = LogLevel.Error, Message = "PaGetto configuration is invalid.")]
    private partial void LogConfigurationInvalid(Exception exception);
}
