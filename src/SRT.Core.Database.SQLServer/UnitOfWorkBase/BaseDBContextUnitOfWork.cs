// Ignore Spelling: SRT

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SRT.Core.Databases;
using SRT.Core.Domain.BaseDomains;
using SRT.Core.Domain.DomainInterfaces;
using System.Linq.Expressions;

namespace SRT.Core.Database.SQLServer.UnitOfWorkBase
{

    public abstract class BaseDBContextUnitOfWork
    {
        private readonly DataBase _dbContext;

        protected BaseDBContextUnitOfWork(DataBase dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        #region Last Inserted ID
        protected async Task<int> GetLastIdAsync()
        {
            FormattableString sql = $"SELECT CAST(SCOPE_IDENTITY() AS int)";
            return await _dbContext.Database
                                   .SqlQuery<int>(sql)
                                   .FirstOrDefaultAsync()
                                   .ConfigureAwait(false);
        }
        #endregion

        #region SaveChanges
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.SaveChangesAsync(cancellationToken)
                                   .ConfigureAwait(false);
        }
        #endregion

        #region Transaction
        public async Task<IDbContextTransaction> SRT_BeginTransactionAsync(CancellationToken cancellationToken = default) => await _dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        public async Task SRT_CommitTransactionAsync(CancellationToken cancellationToken = default) => await _dbContext.Database.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
        public async Task SRT_RollbackTransactionAsync(CancellationToken cancellationToken = default) => await _dbContext.Database.RollbackTransactionAsync(cancellationToken).ConfigureAwait(false);

        public async Task SRT_ExecuteInTransactionAsync(
            Func<Task> action,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await SRT_BeginTransactionAsync(cancellationToken);
            try
            {
                await action().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        public async Task<TResult> SRT_ExecuteInTransactionAsync<TResult>(
            Func<Task<TResult>> func,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await SRT_BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await func().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        #endregion

        public IQueryable<T> SRT_SetPagination<T>(IQueryable<T> data, SearchInput dmSearch)
        {
            var skipt = (dmSearch.pageIndex - 1) * dmSearch.pageSize;
            return data.Skip(skipt).Take(dmSearch.pageSize);
        }

        public IQueryable<T> SRT_OrderBy<T>(IQueryable<T> query, SearchInput dmSearch)
        {
            var orderByFieldName = dmSearch.orderByFieldName;
            var orderByDescending = dmSearch.OrderByDescending;

            // Create a parameter expression for the type T
            var parameter = Expression.Parameter(typeof(T), "x");

            // Create the property access expression
            var property = Expression.Property(parameter, orderByFieldName);

            // Create the lambda expression for ordering
            var orderByExpression = Expression.Lambda(property, parameter);

            // Determine the OrderBy or OrderByDescending method to call
            var methodName = orderByDescending ? "OrderByDescending" : "OrderBy";

            // Create the OrderBy method call
            var orderedQuery = query.Provider.CreateQuery<T>(
                Expression.Call(
                    typeof(Queryable),
                    methodName,
                    new Type[] { typeof(T), property.Type },
                    query.Expression,
                    Expression.Quote(orderByExpression)
                )
            );

            return orderedQuery;
        }

        public async Task<TOut> SRT_GenerateSearchResult<TIn, TOut, TOutList>(
            IQueryable<TIn> query,
            SearchInput dmSearch,
            TOut dmResult,
            Func<TIn, TOutList> DomainToDTO,
            Func<IQueryable<TIn>, IQueryable<TIn>>? AddInclude = null)
            where TIn : class
            where TOut : SearchResult<TOutList>
            where TOutList : class
        {
            dmResult.count = await query.CountAsync().ConfigureAwait(false);

            query = SRT_OrderBy(query, dmSearch);
            query = SRT_SetPagination(query, dmSearch);

            if (AddInclude != null)
                query = AddInclude(query);

            var lstData = await query.ToListAsync().ConfigureAwait(false);

            dmResult.lstData = lstData.Select(x => DomainToDTO(x)).ToList();

            return dmResult;
        }

        public IQueryable<T> Filter<T>(
            IQueryable<T> lst,
            SearchInput dmSearch)
            where T : IDatabaseEntry, IDatabaseEntryActivity
        {
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
    }
}