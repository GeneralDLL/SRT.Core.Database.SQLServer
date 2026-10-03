using SRT.Core.Database.SQLServer.ErrorLog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SRT.Core.Domain;
using SRT.Core.Domain.Diagnostics;
using SRT.Core.Diagnostics;

namespace SRT.Core.Database.SQLServer.ErrorLog.Repository
{
    public class ErrorLogger : IErrorLogger
    {
        private const int MaxTake = 200;
        private const int DefaultTake = 50;

        private readonly ErrorReportDBContext _dbContext;
        private readonly ILogger<ErrorReportDBContext> logger;
        private readonly int _serviceId;
        private readonly string _serviceName;

        public ErrorLogger(ErrorReportDBContext dbContext, ILogger<ErrorReportDBContext> _logger, AppSetting config)
        {
            _dbContext = dbContext;
            logger = _logger;
            _serviceId = config.ServiceId;
            _serviceName = string.IsNullOrWhiteSpace(config.ServiceName)
                ? AppDomain.CurrentDomain.FriendlyName
                : config.ServiceName.Trim();
        }

        public async Task<long> Save_Error_ToDB(Exception exp)
        {
            return await SaveError_ToDB(exp, envelope: null, persistToDb: true);
        }

        public async Task<(Guid ErrorId, long DbId)> Save_Error_WithEnvelope(
            Exception exp,
            DiagnosticEnvelope envelope,
            bool persistToDb)
        {
            envelope.ExceptionType ??= exp.GetType().FullName;
            var dbId = await SaveError_ToDB(exp, envelope, persistToDb);
            return (envelope.ErrorId, dbId);
        }

        public async Task<long> Save_Log(string text)
        {
            return await SaveError_ToDB(new Exception("LOG", new Exception(text, new Exception(DateTime.Now.ToString("G")))), null, true);
        }

        public string GetErrorString(Exception exp) => GetError_String(exp);

        public static string GetError_String(Exception exp)
        {
            string val = "";
            int number = 1;

            while (exp.InnerException != null)
            {
                val += (number + " - " + exp.Message + Environment.NewLine);
                exp = exp.InnerException;
                number++;
            }
            val += Environment.NewLine;
            val += "Message: " + exp.Message + Environment.NewLine;
            val += "StackTrace: " + exp.StackTrace;
            val += "Program: " + AppDomain.CurrentDomain.FriendlyName;
            val += "Date: " + DateTime.Now;

            return val;
        }

        #region Search / Delete by filter

        public async Task<IReadOnlyList<ErrorReportSummary>> SearchByFilterAsync(ErrorReportFilter filter, CancellationToken ct = default)
        {
            filter = Normalize(filter);
            EnsureHasCriteria(filter);

            var take = filter.Take <= 0 ? DefaultTake : Math.Min(filter.Take, MaxTake);
            var skip = Math.Max(0, filter.Skip);

            var query = ApplyFilter(_dbContext.ErrorReports.AsNoTracking(), filter)
                .OrderByDescending(e => e.date)
                .ThenByDescending(e => e.id)
                .Skip(skip)
                .Take(take);

            return await query
                .Select(e => new ErrorReportSummary
                {
                    Id = e.id,
                    ErrorId = e.ErrorId,
                    Date = e.date,
                    ServiceName = e.serviceName,
                    Service = e.Service,
                    Category = e.Category,
                    Severity = e.Severity,
                    Outcome = e.Outcome,
                    ExceptionType = e.ExceptionType,
                    Target = e.Target,
                    CorrelationId = e.CorrelationId,
                    Message = (e.Message1 ?? "") + (e.Message2 ?? "") + (e.Message3 ?? "") + (e.Message4 ?? "") + (e.Message5 ?? ""),
                })
                .ToListAsync(ct);
        }

        public async Task<int> DeleteByFilterAsync(ErrorReportFilter filter, CancellationToken ct = default)
        {
            filter = Normalize(filter);
            EnsureHasCriteria(filter);

            return await ApplyFilter(_dbContext.ErrorReports, filter)
                .ExecuteDeleteAsync(ct);
        }

        private static ErrorReportFilter Normalize(ErrorReportFilter filter)
        {
            ArgumentNullException.ThrowIfNull(filter);

            filter.CorrelationId = NullIfEmpty(filter.CorrelationId);
            filter.Category = NullIfEmpty(filter.Category);
            filter.Service = NullIfEmpty(filter.Service);
            filter.ServiceName = NullIfEmpty(filter.ServiceName);
            filter.Environment = NullIfEmpty(filter.Environment);
            filter.Operation = NullIfEmpty(filter.Operation);
            filter.Severity = NullIfEmpty(filter.Severity);
            filter.Outcome = NullIfEmpty(filter.Outcome);
            filter.ExceptionType = NullIfEmpty(filter.ExceptionType);
            filter.Target = NullIfEmpty(filter.Target);
            filter.MessageContains = NullIfEmpty(filter.MessageContains);
            return filter;
        }

        private static string? NullIfEmpty(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return value.Trim();
        }

        private static void EnsureHasCriteria(ErrorReportFilter filter)
        {
            var hasCriteria =
                filter.ErrorId.HasValue ||
                filter.CorrelationId != null ||
                filter.Category != null ||
                filter.Service != null ||
                filter.ServiceName != null ||
                filter.Environment != null ||
                filter.Operation != null ||
                filter.Severity != null ||
                filter.Outcome != null ||
                filter.ExceptionType != null ||
                filter.Target != null ||
                filter.ServiceId.HasValue ||
                filter.DateFrom.HasValue ||
                filter.DateTo.HasValue ||
                filter.MessageContains != null;

            if (!hasCriteria)
                throw new ArgumentException(
                    "At least one non-paging filter criterion is required for ErrorReport search/delete.",
                    nameof(filter));
        }

        private static IQueryable<ErrorReport> ApplyFilter(IQueryable<ErrorReport> query, ErrorReportFilter filter)
        {
            if (filter.ErrorId.HasValue)
                query = query.Where(e => e.ErrorId == filter.ErrorId);

            if (filter.CorrelationId != null)
                query = query.Where(e => e.CorrelationId == filter.CorrelationId);

            if (filter.Category != null)
                query = query.Where(e => e.Category == filter.Category);

            if (filter.Service != null)
                query = query.Where(e => e.Service == filter.Service);

            if (filter.ServiceName != null)
                query = query.Where(e => e.serviceName == filter.ServiceName);

            if (filter.Environment != null)
                query = query.Where(e => e.Environment == filter.Environment);

            if (filter.Operation != null)
                query = query.Where(e => e.Operation == filter.Operation);

            if (filter.Severity != null)
                query = query.Where(e => e.Severity == filter.Severity);

            if (filter.Outcome != null)
                query = query.Where(e => e.Outcome == filter.Outcome);

            if (filter.ExceptionType != null)
                query = query.Where(e => e.ExceptionType == filter.ExceptionType);

            if (filter.Target != null)
                query = query.Where(e => e.Target == filter.Target);

            if (filter.ServiceId.HasValue)
                query = query.Where(e => e.serviceID == filter.ServiceId.Value);

            if (filter.DateFrom.HasValue)
                query = query.Where(e => e.date >= filter.DateFrom.Value);

            if (filter.DateTo.HasValue)
                query = query.Where(e => e.date <= filter.DateTo.Value);

            if (filter.MessageContains != null)
            {
                var term = filter.MessageContains;
                // Full message concat so phrases spanning MessageN chunk boundaries still match.
                query = query.Where(e =>
                    ((e.Message1 ?? "") + (e.Message2 ?? "") + (e.Message3 ?? "") + (e.Message4 ?? "") + (e.Message5 ?? ""))
                    .Contains(term));
            }

            return query;
        }

        #endregion

        private async Task<long> SaveError_ToDB(Exception exp, DiagnosticEnvelope? envelope, bool persistToDb)
        {
            var ex_main = exp;
            var errorId = envelope?.ErrorId ?? Guid.NewGuid();
            if (envelope is not null)
                envelope.ErrorId = errorId;

            // Always log to console/Serilog first so triage works when Error DB is down
            logger.LogError(
                exp,
                "Diag ErrorId={ErrorId} Category={Category} Operation={Operation} Outcome={Outcome} Target={Target} CorrelationId={CorrelationId} Service={Service} Detail={Detail}",
                errorId,
                envelope?.Category,
                envelope?.Operation,
                envelope?.Outcome,
                envelope?.Target,
                envelope?.CorrelationId,
                envelope?.Service ?? AppDomain.CurrentDomain.FriendlyName,
                envelope?.ToDetailJson());

            if (!persistToDb || (envelope is not null && !envelope.PersistToErrorDb))
                return 0;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

                var data = new ErrorReport();

                var current = exp;
                var count = 0;
                while (current != null && count < 20)
                {
                    set_data(data, current.Message, count++);
                    if (current.InnerException == null)
                        break;
                    current = current.InnerException;
                }

                var innermost = exp;
                while (innermost.InnerException != null)
                    innermost = innermost.InnerException;

                data.Message = innermost.Message;
                data.StackTrace = innermost.StackTrace;
                data.serviceID = _serviceId;
                data.serviceName = _serviceName;
                data.date = DateTime.Now;

                data.ErrorId = errorId;
                data.CorrelationId = Truncate(envelope?.CorrelationId, 100);
                data.Service = Truncate(envelope?.Service ?? AppDomain.CurrentDomain.FriendlyName, 200);
                data.Environment = Truncate(envelope?.Environment, 50);
                data.Version = Truncate(envelope?.Version, 100);
                data.Category = Truncate(envelope?.Category, 50);
                data.Operation = Truncate(envelope?.Operation, 100);
                data.Severity = Truncate(envelope?.Severity, 20);
                data.Outcome = Truncate(envelope?.Outcome, 30);
                data.ExceptionType = Truncate(envelope?.ExceptionType ?? exp.GetType().FullName, 200);
                data.Target = Truncate(envelope?.Target, 300);
                data.DetailJson = envelope?.ToDetailJson();

                _dbContext.ErrorReports.Add(data);
                await _dbContext.SaveChangesAsync(cts.Token);

                return data.id;
            }
            catch (Exception dbEx)
            {
                // Isolation: never cascade; console already has ErrorId
                logger.LogCritical(
                    dbEx,
                    "Error DB persist failed for ErrorId={ErrorId}. Original: {Original}",
                    errorId,
                    GetErrorString(ex_main));
                return 0;
            }
        }

        private static string? Truncate(string? value, int max)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            return value.Length <= max ? value : value.Substring(0, max);
        }

        private void set_data(ErrorReport data, string value, int count)
        {
            switch (count)
            {
                case 0: data.layer1 = value; break;
                case 1: data.layer2 = value; break;
                case 2: data.layer3 = value; break;
                case 3: data.layer4 = value; break;
                case 4: data.layer5 = value; break;
                case 5: data.layer6 = value; break;
                case 6: data.layer7 = value; break;
                case 7: data.layer8 = value; break;
                case 8: data.layer9 = value; break;
                case 9: data.layer10 = value; break;
                case 10: data.layer11 = value; break;
                case 11: data.layer12 = value; break;
                case 12: data.layer13 = value; break;
                case 13: data.layer14 = value; break;
                case 14: data.layer15 = value; break;
                case 15: data.layer16 = value; break;
                case 16: data.layer17 = value; break;
                case 17: data.layer18 = value; break;
                case 18: data.layer19 = value; break;
                case 19: data.layer20 = value; break;
                default:
                    throw new Exception("Not Find Layer");
            }
        }
    }
}
