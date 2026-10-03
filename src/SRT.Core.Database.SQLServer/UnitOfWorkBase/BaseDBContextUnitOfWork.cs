// Ignore Spelling: SRT

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SRT.Core.Databases;
using SRT.Core.Domain.BaseDomains;
using SRT.Core.Domain.DomainInterfaces;
using SRT.Core.Interfaces.Databases;

namespace SRT.Core.Database.SQLServer.UnitOfWorkBase
{
    /// <summary>
    /// SQL Server unit of work. All repositories under one UoW must share this same
    /// <see cref="DataBase"/> instance and must not dispose it.
    /// </summary>
    public abstract class BaseDBContextUnitOfWork : IUnitOfWork
    {
        protected DataBase DbContext { get; }

        protected BaseDBContextUnitOfWork(DataBase dbContext)
        {
            DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        #region SaveChanges

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => DbContext.SaveChangesAsync(cancellationToken);

        #endregion

        #region Transaction

        /// <summary>
        /// SQL-only helpers. Prefer <see cref="ExecuteInTransactionAsync"/> from Core.
        /// </summary>
        public Task<IDbContextTransaction> SRT_BeginTransactionAsync(CancellationToken cancellationToken = default)
            => DbContext.Database.BeginTransactionAsync(cancellationToken);

        public Task SRT_CommitTransactionAsync(CancellationToken cancellationToken = default)
            => DbContext.Database.CommitTransactionAsync(cancellationToken);

        public Task SRT_RollbackTransactionAsync(CancellationToken cancellationToken = default)
            => DbContext.Database.RollbackTransactionAsync(cancellationToken);

        public Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default)
            => ExecuteInTransactionCoreAsync(async () =>
            {
                await action().ConfigureAwait(false);
                return true;
            }, cancellationToken);

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<Task<TResult>> func,
            CancellationToken cancellationToken = default)
            => ExecuteInTransactionCoreAsync(func, cancellationToken);

        /// <summary>Legacy alias for <see cref="ExecuteInTransactionAsync"/>.</summary>
        public Task SRT_ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default)
            => ExecuteInTransactionAsync(action, cancellationToken);

        /// <summary>Legacy alias for <see cref="ExecuteInTransactionAsync{TResult}"/>.</summary>
        public Task<TResult> SRT_ExecuteInTransactionAsync<TResult>(
            Func<Task<TResult>> func,
            CancellationToken cancellationToken = default)
            => ExecuteInTransactionAsync(func, cancellationToken);

        private async Task<TResult> ExecuteInTransactionCoreAsync<TResult>(
            Func<Task<TResult>> func,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(func);

            var strategy = DbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await DbContext.Database
                    .BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    var result = await func().ConfigureAwait(false);
                    await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }).ConfigureAwait(false);
        }

        #endregion

        #region Search / query

        public IQueryable<T> SRT_SetPagination<T>(IQueryable<T> data, SearchInput dmSearch)
            => PaginationHelper.Paginate(data, dmSearch);

        /// <summary>
        /// Orders by <see cref="SearchInput.orderByFieldName"/>.
        /// When <paramref name="allowedFields"/> is null, the path is trusted/internal.
        /// For user input, pass a whitelist; unknown fields throw.
        /// Appends a PK (<c>id</c>) tie-break when the type has that property and it is not already the order field.
        /// </summary>
        public IQueryable<T> SRT_OrderBy<T>(
            IQueryable<T> query,
            SearchInput dmSearch,
            IReadOnlyCollection<string>? allowedFields = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(dmSearch);

            var orderByFieldName = string.IsNullOrWhiteSpace(dmSearch.orderByFieldName)
                ? "id"
                : dmSearch.orderByFieldName;

            if (allowedFields is not null)
            {
                if (!allowedFields.Contains(orderByFieldName, StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException(
                        $"Order field '{orderByFieldName}' is not in the allowed list.",
                        nameof(dmSearch));
            }

            var property = typeof(T).GetProperty(
                orderByFieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);

            if (property is null)
                throw new ArgumentException(
                    $"Type '{typeof(T).Name}' has no public property named '{orderByFieldName}'.",
                    nameof(dmSearch));

            // Use the actual property name for the expression (respect CLR casing).
            orderByFieldName = property.Name;

            query = ApplyOrder(query, orderByFieldName, dmSearch.OrderByDescending, thenBy: false);

            var idProperty = typeof(T).GetProperty(
                "id",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);

            if (idProperty is not null
                && !string.Equals(idProperty.Name, orderByFieldName, StringComparison.OrdinalIgnoreCase))
            {
                query = ApplyOrder(query, idProperty.Name, descending: false, thenBy: true);
            }

            return query;
        }

        public async Task<TOut> SRT_GenerateSearchResult<TIn, TOut, TOutList>(
            IQueryable<TIn> query,
            SearchInput dmSearch,
            TOut dmResult,
            Func<TIn, TOutList> DomainToDTO,
            Func<IQueryable<TIn>, IQueryable<TIn>>? AddInclude = null,
            IReadOnlyCollection<string>? allowedOrderFields = null,
            CancellationToken cancellationToken = default)
            where TIn : class
            where TOut : SearchResult<TOutList>
            where TOutList : class
        {
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(dmSearch);
            ArgumentNullException.ThrowIfNull(dmResult);
            ArgumentNullException.ThrowIfNull(DomainToDTO);

            // Pipeline: include? → (caller filter already applied) → count → order(+PK) → validate Skip/Take → page
            if (AddInclude is not null)
                query = AddInclude(query);

            dmResult.count = await query.CountAsync(cancellationToken).ConfigureAwait(false);

            query = SRT_OrderBy(query, dmSearch, allowedOrderFields);
            PaginationHelper.Validate(dmSearch);
            query = SRT_SetPagination(query, dmSearch);

            var lstData = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
            dmResult.lstData = lstData.Select(x => DomainToDTO(x)).ToList();
            return dmResult;
        }

        public IQueryable<T> Filter<T>(IQueryable<T> lst, SearchInput dmSearch)
            where T : IDatabaseEntry, IDatabaseEntryActivity
        {
            ArgumentNullException.ThrowIfNull(lst);
            ArgumentNullException.ThrowIfNull(dmSearch);

            if (dmSearch.isActive.HasValue)
            {
                var a = dmSearch.isActive.Value;
                lst = lst.Where(q => q.isActive == a);
            }

            if (dmSearch.date_Start.HasValue)
            {
                var a = dmSearch.date_Start.Value;
                lst = lst.Where(q => q.date_Create >= a);
            }

            if (dmSearch.date_End.HasValue)
            {
                var a = dmSearch.date_End.Value;
                lst = lst.Where(q => q.date_Create < a);
            }

            return lst;
        }

        private static IQueryable<T> ApplyOrder<T>(IQueryable<T> query, string propertyName, bool descending, bool thenBy)
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            var propertyAccess = Expression.Property(parameter, propertyName);
            var orderByExpression = Expression.Lambda(propertyAccess, parameter);

            string methodName;
            if (thenBy)
                methodName = descending ? "ThenByDescending" : "ThenBy";
            else
                methodName = descending ? "OrderByDescending" : "OrderBy";

            return query.Provider.CreateQuery<T>(
                Expression.Call(
                    typeof(Queryable),
                    methodName,
                    new[] { typeof(T), propertyAccess.Type },
                    query.Expression,
                    Expression.Quote(orderByExpression)));
        }

        #endregion
    }
}
