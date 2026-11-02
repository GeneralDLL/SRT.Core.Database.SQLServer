using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRT.Core.Diagnostics;

namespace SRT.Core.Database.SQLServer.ErrorLog.Retention
{
    public sealed class ErrorLogRetentionOptions
    {
        public const string SectionName = "SRTCore_ErrorLog:Retention";

        public bool Enabled { get; set; } = true;
        public int Days { get; set; } = 1;
    }

    /// <summary>Deletes old ErrorReports every hour, in batches, so one run never holds a long lock.</summary>
    public sealed class ErrorLogRetentionService : BackgroundService
    {
        private const int BatchSize = 5000;
        private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

        private readonly IServiceScopeFactory _scopes;
        private readonly ErrorLogRetentionOptions _options;
        private readonly ILogger<ErrorLogRetentionService> _logger;

        public ErrorLogRetentionService(
            IServiceScopeFactory scopes,
            IOptions<ErrorLogRetentionOptions> options,
            ILogger<ErrorLogRetentionService> logger)
        {
            _scopes = scopes;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
                return;

            try
            {
                using var timer = new PeriodicTimer(Interval);
                do
                {
                    try
                    {
                        using var scope = _scopes.CreateScope();
                        var errors = scope.ServiceProvider.GetRequiredService<IErrorLogger>();
                        var deleted = await errors.PurgeOlderThanAsync(_options.Days, BatchSize, stoppingToken);
                        _logger.LogInformation("Error log retention deleted {Deleted} rows older than {Days} days.", deleted, _options.Days);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogWarning(exception, "Error log retention failed.");
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Stopping is normal. If host startup failed, a cancelled ExecuteAsync is logged as a failure and hides the real error.
            }
        }
    }
}
