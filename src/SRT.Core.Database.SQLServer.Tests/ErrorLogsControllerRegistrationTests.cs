using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SRT.Core.Authentication;
using SRT.Core.Domain;
using SRT.Core.SystemEnums;

namespace SRT.Core.Database.SQLServer.Tests;

public sealed class ErrorLogsControllerRegistrationTests
{
    [Fact]
    public async Task Sql_store_exposes_every_route_behind_devops_auth()
    {
        var endpoints = await EndpointsAsync(useErrorLogSql: true);

        var routes = endpoints.Select(endpoint => endpoint.RoutePattern.RawText).ToList();
        Assert.Contains("devops/error-logs/Search", routes);
        Assert.Contains("devops/error-logs/{id:long}", routes);
        Assert.Contains("devops/error-logs/ByCorrelation/{correlationId}", routes);
        Assert.Contains("devops/error-logs/Facets", routes);
        Assert.Contains("devops/error-logs/Stats", routes);
        Assert.Contains("devops/error-logs/Export", routes);
        Assert.Contains("devops/error-logs/Delete", routes);
        Assert.All(endpoints, endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<CheckAuthMiddleWareAttribute>()));
    }

    [Fact]
    public async Task Without_the_sql_store_no_route_exists()
    {
        Assert.Empty(await EndpointsAsync(useErrorLogSql: false));
    }

    private static async Task<List<RouteEndpoint>> EndpointsAsync(bool useErrorLogSql)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers();
        var setting = new AppSetting
        {
            UseErrorLogSQL = useErrorLogSql,
            UseCashStructure = CashType.None,
            ConnectionStrings = new AppConnectionString
            {
                SRTCore_ErrorSQL = new GeneralConnectionString { connectionType = "sql", host = "localhost", port = "1433", dbName = "Unused" },
            },
        };

        await new SqlServerBasicConfigurations().SRT_ConfigBuilder(builder, setting);

        var app = builder.Build();
        app.MapControllers();
        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("devops/error-logs", StringComparison.Ordinal) == true)
            .ToList();
    }
}
