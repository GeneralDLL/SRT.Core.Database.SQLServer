using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SRT.Core.Authentication;
using SRT.Core.Diagnostics;
using SRT.Core.Domain.Diagnostics;
using SRT.Core.SYS_Controllers;
using SRT.Core.SystemEnums;

namespace SRT.Core.Database.SQLServer.ErrorLog.Controllers
{
    public static class ErrorLogAccess
    {
        public const string View = "devops.errorlogs.View";
        public const string Export = "devops.errorlogs.Export";
        public const string Delete = "devops.errorlogs.Delete";
    }

    // Internal on purpose: default discovery skips it, so only ErrorLogsControllerFeatureProvider can expose it.
    [ApiController]
    [Route("devops/error-logs")]
    internal sealed class SRTErrorLogsController : SRTBaseControllerSystem
    {
        private const string FailedMessage = "خواندن لاگ خطا انجام نشد. دوباره تلاش کنید.";
        private const string NotFoundMessage = "لاگ پیدا نشد.";
        private const string DateRangeRequiredMessage = "برای حذف، بازهٔ زمانی لازم است.";

        private readonly IErrorLogger _errors;

        public SRTErrorLogsController(IErrorLogger errors)
        {
            _errors = errors;
        }

        [HttpPost("Search")]
        [CheckAuthMiddleWare(TypeAuthenticationController.DevOps, requiredAccess: ErrorLogAccess.View)]
        public Task<IActionResult> Search([FromBody] ErrorReportFilter filter, CancellationToken ct)
            => RunAsync(async () => Response_OK(data: await _errors.SearchPageAsync(filter, ct)));

        [HttpGet("{id:long}")]
        [CheckAuthMiddleWare(TypeAuthenticationController.DevOps, requiredAccess: ErrorLogAccess.View)]
        public Task<IActionResult> Get(long id, CancellationToken ct)
            => RunAsync(async () =>
            {
                var detail = await _errors.GetByIdAsync(id, ct);
                return detail is null ? Response_Error_NotFoundInputError(NotFoundMessage) : Response_OK(data: detail);
            });

        [HttpGet("ByCorrelation/{correlationId}")]
        [CheckAuthMiddleWare(TypeAuthenticationController.DevOps, requiredAccess: ErrorLogAccess.View)]
        public Task<IActionResult> ByCorrelation(string correlationId, CancellationToken ct)
            => RunAsync(async () => Response_OK(data: await _errors.GetByCorrelationAsync(correlationId, ct)));

        [HttpPost("Facets")]
        [CheckAuthMiddleWare(TypeAuthenticationController.DevOps, requiredAccess: ErrorLogAccess.View)]
        public Task<IActionResult> Facets([FromBody] ErrorReportFilter filter, CancellationToken ct)
            => RunAsync(async () => Response_OK(data: await _errors.FacetsAsync(filter, ct)));

        [HttpPost("Stats")]
        [CheckAuthMiddleWare(TypeAuthenticationController.DevOps, requiredAccess: ErrorLogAccess.View)]
        public Task<IActionResult> Stats([FromBody] ErrorReportFilter filter, CancellationToken ct)
            => RunAsync(async () => Response_OK(data: await _errors.StatsAsync(filter, ct)));

        [HttpPost("Export")]
        [CheckAuthMiddleWare(TypeAuthenticationController.DevOps, requiredAccess: ErrorLogAccess.Export)]
        public Task<IActionResult> Export([FromBody] ErrorReportFilter filter, CancellationToken ct)
            => RunAsync(async () => File(
                await _errors.ExportCsvAsync(filter, ct),
                "text/csv",
                $"error-logs-{DateTime.Now:yyyyMMdd-HHmmss}.csv"));

        [HttpPost("Delete")]
        [CheckAuthMiddleWare(TypeAuthenticationController.DevOps, requiredAccess: ErrorLogAccess.Delete)]
        public Task<IActionResult> Delete([FromBody] ErrorReportFilter filter, CancellationToken ct)
            => RunAsync(async () =>
            {
                if (filter.LastHours is not > 0 && (filter.DateFrom is null || filter.DateTo is null))
                    return Response_Error_BadRequest(DateRangeRequiredMessage);

                var deleted = await _errors.DeleteByFilterAsync(filter, ct);
                Logger.LogWarning(
                    "Error logs deleted by user {UserId}: {Deleted} rows, filter {Filter}",
                    dmUser?.id,
                    deleted,
                    JsonSerializer.Serialize(filter));
                return Response_OK(data: new { deleted });
            });

        private async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action)
        {
            try
            {
                return await action();
            }
            catch (ArgumentException exception)
            {
                return Response_Error_BadRequest(exception.Message);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return await Response_Error_ControlInternalServerError(FailedMessage, exception);
            }
        }
    }
}
