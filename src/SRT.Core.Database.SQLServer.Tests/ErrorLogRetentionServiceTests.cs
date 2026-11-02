using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SRT.Core.Database.SQLServer.ErrorLog.Retention;
using SRT.Core.Diagnostics;
using SRT.Core.Domain.Diagnostics;

namespace SRT.Core.Database.SQLServer.Tests;

public sealed class ErrorLogRetentionServiceTests
{
    public static TheoryData<Exception?> RunResults => new()
    {
        (Exception?)null,
        new InvalidOperationException("sql down"),
        new TaskCanceledException("sql timeout"),
    };

    [Theory]
    [MemberData(nameof(RunResults))]
    public async Task One_run_never_ends_the_loop_and_stopping_is_not_a_failure(Exception? runError)
    {
        var errors = new FakeErrorLogger(runError);
        using var provider = new ServiceCollection().AddScoped<IErrorLogger>(_ => errors).BuildServiceProvider();
        using var service = new ErrorLogRetentionService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ErrorLogRetentionOptions()),
            NullLogger<ErrorLogRetentionService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await errors.Purged.Task;
        await Task.WhenAny(service.ExecuteTask!, Task.Delay(200));
        Assert.False(service.ExecuteTask!.IsCompleted);

        await service.StopAsync(CancellationToken.None);
        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);
    }

    private sealed class FakeErrorLogger(Exception? runError) : IErrorLogger
    {
        public TaskCompletionSource Purged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int> PurgeOlderThanAsync(int days, int batch, CancellationToken ct = default)
        {
            Purged.TrySetResult();
            return runError is null ? Task.FromResult(0) : Task.FromException<int>(runError);
        }

        public Task<long> Save_Error_ToDB(Exception exp) => throw new NotSupportedException();
        public Task<(Guid ErrorId, long DbId)> Save_Error_WithEnvelope(Exception exp, DiagnosticEnvelope envelope, bool persistToDb) => throw new NotSupportedException();
        public Task<long> Save_Log(string text) => throw new NotSupportedException();
        public string GetErrorString(Exception exp) => throw new NotSupportedException();
        public Task<IReadOnlyList<ErrorReportSummary>> SearchByFilterAsync(ErrorReportFilter filter, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> DeleteByFilterAsync(ErrorReportFilter filter, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
