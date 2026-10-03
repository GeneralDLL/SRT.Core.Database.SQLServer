using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SRT.Core.Databases;
using SRT.Core.Domain.BaseDomains;
using SRT.Core.Domain.DomainInterfaces;
using SRT.Core.Interfaces.Databases;

namespace SRT.Core.Database.SQLServer.RepositoriesBase
{
    /// <summary>
    /// Shared SQL Server repository helpers. Holds the UoW's <see cref="DataBase"/> instance
    /// and never disposes it. Prefer <see cref="BaseDBContextRepository{TEntity,TKey}"/> for
    /// single-entity repositories that implement <see cref="IRepository{TEntity,TKey}"/>.
    /// </summary>
    public abstract class BaseDBContextRepository
    {
        protected DataBase DbContext { get; }

        protected BaseDBContextRepository(DataBase dbContext)
        {
            DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        #region GetById

        protected Task<TEntity?> GetByIdAsync<TEntity, TKey>(
            TKey id,
            CancellationToken cancellationToken = default)
            where TEntity : class, IDatabaseEntry, IDatabaseEntryID<TKey>
        {
            return DbContext.Set<TEntity>()
                .AsNoTracking()
                .FirstOrDefaultAsync(BuildIdEquals<TEntity, TKey>(id), cancellationToken);
        }

        #endregion

        #region Add/Update (Single)

        /// <summary>
        /// When <paramref name="autoSave"/> is true, saves all pending changes on the shared context.
        /// </summary>
        protected async Task<TEntity> AddOrUpdateAsync<TEntity, TKey>(
            TEntity entity,
            IUserInfo? adminInfoChange = null,
            bool autoSave = true,
            CancellationToken cancellationToken = default)
            where TEntity : class, IDatabaseEntry, IDatabaseEntryID<TKey>
        {
            ArgumentNullException.ThrowIfNull(entity);
            var dbSet = DbContext.Set<TEntity>();

            ApplyAdminInfo(entity, adminInfoChange);
            entity.date_Update = DateTime.Now;

            var isInsert = await IsInsertAsync<TEntity, TKey>(entity, cancellationToken).ConfigureAwait(false);
            if (isInsert)
            {
                entity.date_Create = DateTime.Now;
                SetGUID(entity);
                dbSet.Add(entity);
            }
            else
            {
                var tracked = await dbSet.FindAsync(new object?[] { entity.id! }, cancellationToken).ConfigureAwait(false);
                if (tracked is null)
                {
                    dbSet.Update(entity);
                }
                else if (!ReferenceEquals(tracked, entity))
                {
                    DbContext.Entry(tracked).CurrentValues.SetValues(entity);
                    ApplyAdminInfo(tracked, adminInfoChange);
                    tracked.date_Update = entity.date_Update;
                }
            }

            if (autoSave)
                await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return entity;
        }

        #endregion

        #region Add/Update (Range)

        /// <summary>
        /// Stages all entities without intermediate saves, then one SaveChanges at the end.
        /// Multiple default keys (e.g. int id == 0) are allowed; duplicate assigned keys throw.
        /// </summary>
        protected async Task AddOrUpdateRangeAsync<TEntity, TKey>(
            IEnumerable<TEntity> entities,
            IUserInfo? adminInfoChange = null,
            CancellationToken cancellationToken = default)
            where TEntity : class, IDatabaseEntry, IDatabaseEntryID<TKey>
        {
            ArgumentNullException.ThrowIfNull(entities);
            var entityList = entities.ToList();
            EnsureNoDuplicateAssignedKeys<TEntity, TKey>(entityList);

            foreach (var entity in entityList)
            {
                await AddOrUpdateAsync<TEntity, TKey>(entity, adminInfoChange, autoSave: false, cancellationToken)
                    .ConfigureAwait(false);
            }

            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        [Obsolete("Use AddOrUpdateRangeAsync instead.")]
        protected Task AddOrUpdateListAsync<TEntity, TKey>(
            IEnumerable<TEntity> entities,
            IUserInfo? adminInfoChange = null,
            CancellationToken cancellationToken = default)
            where TEntity : class, IDatabaseEntry, IDatabaseEntryID<TKey>
            => AddOrUpdateRangeAsync<TEntity, TKey>(entities, adminInfoChange, cancellationToken);

        #endregion

        #region Add/Update/Delete (List)

        protected async Task AddUpdateDeleteListAsync<TEntity, TKey>(
            IEnumerable<TEntity> newEntities,
            IEnumerable<TEntity> oldEntities,
            IUserInfo? adminInfoChange = null,
            CancellationToken cancellationToken = default)
            where TEntity : class, IDatabaseEntry, IDatabaseEntryID<TKey>
        {
            ArgumentNullException.ThrowIfNull(newEntities);
            ArgumentNullException.ThrowIfNull(oldEntities);

            var newList = newEntities.ToList();
            var oldList = oldEntities.ToList();
            EnsureNoDuplicateAssignedKeys<TEntity, TKey>(newList);

            var dbSet = DbContext.Set<TEntity>();
            var comparer = EqualityComparer<TKey>.Default;

            foreach (var entity in newList)
            {
                await AddOrUpdateAsync<TEntity, TKey>(entity, adminInfoChange, autoSave: false, cancellationToken)
                    .ConfigureAwait(false);
            }

            var toDelete = oldList
                .Where(o => newList.All(n => !comparer.Equals(n.id, o.id)))
                .ToList();

            if (toDelete.Count > 0)
                dbSet.RemoveRange(toDelete);

            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Delete by filter (immediate)

        /// <summary>
        /// Executes immediately via ExecuteDelete; not deferred by autoSave.
        /// </summary>
        protected Task<int> DeleteByFilterAsync<TEntity>(
            Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(predicate);
            return DbContext.Set<TEntity>()
                .Where(predicate)
                .ExecuteDeleteAsync(cancellationToken);
        }

        #endregion

        #region Pagination / ChangeTracker

        public IQueryable<T> Paginate<T>(IQueryable<T> query, int pageIndex, int pageSize)
            => PaginationHelper.Paginate(query, pageIndex, pageSize);

        /// <summary>
        /// Clears the change tracker only when the caller explicitly requests it.
        /// Does not run automatically between repository operations.
        /// </summary>
        protected void ClearChangeTracker() => DbContext.ChangeTracker.Clear();

        #endregion

        #region Change Activation

        public async Task ChangeActivationAsync<TEntity>(
            int id,
            bool isActive,
            IUserInfo? adminInfoChange,
            CancellationToken cancellationToken = default)
            where TEntity : class, IDatabaseEntry, IDatabaseEntryID, IDatabaseEntryActivity
        {
            var dm = await DbContext.Set<TEntity>()
                .Where(q => q.id == id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (dm is null)
                return;

            dm.isActive = isActive;
            await AddOrUpdateAsync<TEntity, int>(dm, adminInfoChange, autoSave: true, cancellationToken)
                .ConfigureAwait(false);
        }

        [Obsolete("Use ChangeActivationAsync.")]
        public Task ChangeActivation<TEntity>(
            DbSet<TEntity> dbSet,
            int id,
            bool isActive,
            IUserInfo? adminInfoChange)
            where TEntity : class, IDatabaseEntry, IDatabaseEntryID, IDatabaseEntryActivity
            => ChangeActivationAsync<TEntity>(id, isActive, adminInfoChange);

        #endregion

        #region Key / helpers

        protected static bool IsDefaultKey<TKey>(TKey key)
            => EqualityComparer<TKey>.Default.Equals(key, default!);

        protected static void EnsureNoDuplicateAssignedKeys<TEntity, TKey>(IReadOnlyCollection<TEntity> entities)
            where TEntity : class, IDatabaseEntryID<TKey>
        {
            var assigned = entities.Where(e => !IsDefaultKey(e.id)).Select(e => e.id!).ToList();
            if (assigned.Count != assigned.Distinct().Count())
                throw new ArgumentException("Duplicate assigned keys are not allowed in the same range.", nameof(entities));
        }

        protected async Task<bool> IsInsertAsync<TEntity, TKey>(
            TEntity entity,
            CancellationToken cancellationToken)
            where TEntity : class, IDatabaseEntry, IDatabaseEntryID<TKey>
        {
            if (IsDefaultKey(entity.id))
                return true;

            return !await DbContext.Set<TEntity>()
                .AsNoTracking()
                .AnyAsync(BuildIdEquals<TEntity, TKey>(entity.id), cancellationToken)
                .ConfigureAwait(false);
        }

        protected static Expression<Func<TEntity, bool>> BuildIdEquals<TEntity, TKey>(TKey id)
            where TEntity : class, IDatabaseEntryID<TKey>
        {
            var parameter = Expression.Parameter(typeof(TEntity), "e");
            var property = Expression.Property(parameter, nameof(IDatabaseEntryID<TKey>.id));
            var constant = Expression.Constant(id, typeof(TKey));
            var body = Expression.Equal(property, constant);
            return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
        }

        private static void SetGUID<T>(T entity) where T : class, IDatabaseEntry
        {
            var guidProperty = typeof(T)
                .GetProperties()
                .FirstOrDefault(p => p.PropertyType == typeof(Guid) && p.CanWrite);

            if (guidProperty is null)
                return;

            var current = guidProperty.GetValue(entity);
            if (current is Guid g && g == Guid.Empty)
                guidProperty.SetValue(entity, Guid.NewGuid());
        }

        private static void ApplyAdminInfo<T>(T entity, IUserInfo? adminInfo) where T : class, IDatabaseEntry
        {
            if (entity is IDatabaseEntryAdmin adminEntity)
            {
                adminEntity.id_AdminChange = adminInfo?.id ?? 0;
                adminEntity.fullName_AdminChange = adminInfo?.GetFullName() ?? "SYSTEM";
            }
        }

        #endregion
    }

    /// <summary>
    /// Typed SQL Server repository implementing <see cref="IRepository{TEntity,TKey}"/>.
    /// </summary>
    public abstract class BaseDBContextRepository<TEntity, TKey> : BaseDBContextRepository, IRepository<TEntity, TKey>
        where TEntity : class, IDatabaseEntry, IDatabaseEntryID<TKey>
    {
        protected BaseDBContextRepository(DataBase dbContext) : base(dbContext)
        {
        }

        public Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
            => GetByIdAsync<TEntity, TKey>(id, cancellationToken);

        public Task<TEntity> AddOrUpdateAsync(
            TEntity entity,
            IUserInfo? adminInfoChange = null,
            bool autoSave = true,
            CancellationToken cancellationToken = default)
            => AddOrUpdateAsync<TEntity, TKey>(entity, adminInfoChange, autoSave, cancellationToken);

        public Task AddOrUpdateRangeAsync(
            IEnumerable<TEntity> entities,
            IUserInfo? adminInfoChange = null,
            CancellationToken cancellationToken = default)
            => AddOrUpdateRangeAsync<TEntity, TKey>(entities, adminInfoChange, cancellationToken);

        public Task<int> DeleteByFilterAsync(
            Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default)
            => DeleteByFilterAsync<TEntity>(predicate, cancellationToken);
    }
}
