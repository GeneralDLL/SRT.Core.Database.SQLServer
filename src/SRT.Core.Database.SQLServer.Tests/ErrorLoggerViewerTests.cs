using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SRT.Core.Database.SQLServer.ErrorLog;
using SRT.Core.Database.SQLServer.ErrorLog.Domain;
using SRT.Core.Database.SQLServer.ErrorLog.Repository;
using SRT.Core.Domain;
using SRT.Core.Domain.Diagnostics;

namespace SRT.Core.Database.SQLServer.Tests;

public sealed class ErrorLoggerViewerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ErrorReportDBContext _database;
    private readonly ErrorLogger _errors;

    public ErrorLoggerViewerTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<ErrorReportDBContext>().UseSqlite(_connection).Options;
        _database = new ErrorReportDBContext(options, new AppConnectionString());
        _database.Database.EnsureCreated();
        _errors = new ErrorLogger(_database, NullLogger<ErrorReportDBContext>.Instance, new AppSetting());
    }

    public void Dispose()
    {
        _database.Dispose();
        _connection.Dispose();
    }

    private ErrorReport Add(
        string message = "boom",
        string severity = "Error",
        double hoursAgo = 1,
        string? operation = null,
        string? target = null,
        string? stackTrace = null,
        string? correlationId = null,
        string? version = null,
        string? legacyCategory = null,
        string? detailJson = null)
    {
        var row = new ErrorReport
        {
            Message = message,
            Severity = severity,
            date = DateTime.Now.AddHours(-hoursAgo),
            Operation = operation,
            Target = target,
            StackTrace = stackTrace,
            CorrelationId = correlationId,
            Version = version,
            category = legacyCategory,
            DetailJson = detailJson,
            ErrorId = Guid.NewGuid(),
        };
        _database.ErrorReports.Add(row);
        _database.SaveChanges();
        return row;
    }

    [Fact]
    public async Task Search_without_range_returns_only_last_24_hours()
    {
        Add(message: "fresh", hoursAgo: 2);
        Add(message: "old", hoursAgo: 30);

        var result = await _errors.SearchPageAsync(new ErrorReportFilter());

        Assert.Equal(1, result.Total);
        Assert.Equal("fresh", Assert.Single(result.Items).Message);
    }

    [Fact]
    public async Task LastHours_replaces_the_explicit_range()
    {
        Add(message: "fresh", hoursAgo: 2);
        Add(message: "old", hoursAgo: 5);

        var result = await _errors.SearchPageAsync(new ErrorReportFilter
        {
            LastHours = 3,
            DateFrom = DateTime.Now.AddDays(-10),
            DateTo = DateTime.Now,
        });

        Assert.Equal("fresh", Assert.Single(result.Items).Message);
    }

    [Fact]
    public async Task Search_pages_and_keeps_the_total()
    {
        for (var i = 0; i < 25; i++)
            Add(message: "row" + i, hoursAgo: 1 + (i / 100.0));

        var result = await _errors.SearchPageAsync(new ErrorReportFilter { Page = 2, PageSize = 10 });

        Assert.Equal(25, result.Total);
        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(10, result.Items.Count);
        Assert.Equal("row10", result.Items[0].Message);
    }

    [Fact]
    public async Task PageSize_is_clamped_to_100()
    {
        var result = await _errors.SearchPageAsync(new ErrorReportFilter { PageSize = 5000 });

        Assert.Equal(100, result.PageSize);
    }

    [Fact]
    public async Task Sort_by_severity_ranks_critical_first()
    {
        Add(severity: "Warning");
        Add(severity: "Critical");
        Add(severity: "Error");

        var result = await _errors.SearchPageAsync(new ErrorReportFilter { SortBy = "severity" });

        Assert.Equal(["Critical", "Error", "Warning"], result.Items.Select(item => item.Severity));
    }

    [Fact]
    public async Task Sort_by_date_ascending()
    {
        Add(message: "newer", hoursAgo: 1);
        Add(message: "older", hoursAgo: 2);

        var result = await _errors.SearchPageAsync(new ErrorReportFilter { SortDir = "asc" });

        Assert.Equal("older", result.Items[0].Message);
    }

    [Fact]
    public async Task Filters_match_the_requested_columns()
    {
        var match = Add(
            message: "needle in message",
            operation: "POST orders/{id}",
            target: "/orders/5",
            stackTrace: "at Foo.Bar()",
            version: "1.2.3",
            legacyCategory: "legacy");
        Add(message: "other");

        Assert.Equal(match.id, Assert.Single((await _errors.SearchPageAsync(new ErrorReportFilter { DbId = match.id })).Items).Id);
        Assert.Single((await _errors.SearchPageAsync(new ErrorReportFilter { MessageContains = "needle" })).Items);
        Assert.Single((await _errors.SearchPageAsync(new ErrorReportFilter { OperationContains = "orders" })).Items);
        Assert.Single((await _errors.SearchPageAsync(new ErrorReportFilter { TargetContains = "/orders" })).Items);
        Assert.Single((await _errors.SearchPageAsync(new ErrorReportFilter { StackTraceContains = "Foo.Bar" })).Items);
        Assert.Single((await _errors.SearchPageAsync(new ErrorReportFilter { Version = "1.2.3" })).Items);
        Assert.Single((await _errors.SearchPageAsync(new ErrorReportFilter { LegacyCategory = "legacy" })).Items);
    }

    [Fact]
    public async Task Message_search_finds_text_across_chunk_boundaries()
    {
        Add(message: new string('x', 3985) + "needle" + new string('y', 10));

        var result = await _errors.SearchPageAsync(new ErrorReportFilter { MessageContains = "needle" });

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetById_returns_the_chain_and_parsed_detail()
    {
        var row = Add(detailJson: """{"StatusCode":500}""");
        row.layer1 = "outer";
        row.layer2 = "inner";
        _database.SaveChanges();

        var detail = await _errors.GetByIdAsync(row.id);

        Assert.Equal(["outer", "inner"], detail!.ExceptionChain);
        Assert.Contains("500", detail.Detail!.ToString());
        Assert.Null(await _errors.GetByIdAsync(row.id + 1));
    }

    [Fact]
    public async Task GetById_keeps_broken_detail_json_as_raw_text()
    {
        var row = Add(detailJson: "{broken");

        var detail = await _errors.GetByIdAsync(row.id);

        Assert.Equal("{broken", detail!.DetailJson);
        Assert.Null(detail.Detail);
    }

    [Fact]
    public async Task GetByCorrelation_ignores_the_date_range()
    {
        Add(correlationId: "trace", hoursAgo: 100);
        Add(correlationId: "trace", hoursAgo: 1);
        Add(correlationId: "other");

        Assert.Equal(2, (await _errors.GetByCorrelationAsync("trace")).Count);
    }

    [Fact]
    public async Task Facets_count_values_inside_the_range()
    {
        Add(severity: "Error");
        Add(severity: "Error");
        Add(severity: "Critical");
        Add(severity: "Warning", hoursAgo: 40);

        var facets = await _errors.FacetsAsync(new ErrorReportFilter());

        Assert.Equal("Error", facets.Severity[0].Value);
        Assert.Equal(2, facets.Severity[0].Count);
        Assert.Equal(2, facets.Severity.Count);
    }

    [Fact]
    public async Task Stats_use_hour_buckets_up_to_48_hours()
    {
        Add(severity: "Error", hoursAgo: 1);
        Add(severity: "Critical", hoursAgo: 1);

        var stats = await _errors.StatsAsync(new ErrorReportFilter());

        Assert.Equal("hour", stats.Bucket);
        Assert.Equal(2, stats.Items.Sum(item => item.Total));
    }

    [Fact]
    public async Task Stats_use_day_buckets_for_longer_ranges()
    {
        Add(severity: "Error", hoursAgo: 60);
        Add(severity: "Error", hoursAgo: 60);

        var stats = await _errors.StatsAsync(new ErrorReportFilter { LastHours = 24 * 7 });

        Assert.Equal("day", stats.Bucket);
        var bucket = Assert.Single(stats.Items);
        Assert.Equal(TimeSpan.Zero, bucket.Start.TimeOfDay);
        Assert.Equal(2, bucket.BySeverity["Error"]);
    }

    [Fact]
    public async Task Export_writes_bom_and_neutralizes_formulas()
    {
        Add(message: "=cmd()");

        var bytes = await _errors.ExportCsvAsync(new ErrorReportFilter());

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Contains("\"'=cmd()\"", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Delete_without_criteria_is_refused()
    {
        Add();

        await Assert.ThrowsAsync<ArgumentException>(() => _errors.DeleteByFilterAsync(new ErrorReportFilter()));
        Assert.Equal(1, await _database.ErrorReports.CountAsync());
    }

    [Fact]
    public async Task Delete_removes_only_matching_rows_and_ignores_paging()
    {
        Add(severity: "Error");
        Add(severity: "Error");
        Add(severity: "Critical");

        var deleted = await _errors.DeleteByFilterAsync(new ErrorReportFilter { Severity = "Error", PageSize = 1 });

        Assert.Equal(2, deleted);
        Assert.Equal("Critical", (await _database.ErrorReports.SingleAsync()).Severity);
    }

    [Fact]
    public async Task Purge_deletes_old_rows_in_batches()
    {
        for (var i = 0; i < 5; i++)
            Add(hoursAgo: 30);
        Add(hoursAgo: 2);

        var deleted = await _errors.PurgeOlderThanAsync(days: 1, batch: 2);

        Assert.Equal(5, deleted);
        Assert.Equal(1, await _database.ErrorReports.CountAsync());
    }

    [Fact]
    public async Task Purge_refuses_a_zero_day_window()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _errors.PurgeOlderThanAsync(days: 0, batch: 100));
    }
}
