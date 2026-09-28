using Microsoft.EntityFrameworkCore;
using SRT.Core.Databases;
using SRT.Core.Domain.DomainInterfaces;


namespace SRT.Core.Database.SQLServer.RepositoriesBase
{
    public abstract class BaseDBContextRepository
    {
        private readonly DataBase _dbContext;

        protected BaseDBContextRepository(DataBase dbContext)
        {
            _dbContext = dbContext;
        }

        #region Get Last ID
        protected async Task<int> GetLastIDAsync()
        {
            FormattableString sql = $"SELECT CAST(SCOPE_IDENTITY() AS int)";
            return await _dbContext.Database.SqlQuery<int>(sql).FirstOrDefaultAsync();
        }
        #endregion

        #region Add/Update (Single)
        protected async Task<T> AddOrUpdateAsync<T>(DbSet<T> dbSet, T entity, IUserInfo? adminInfoChange = null)
            where T : class, IDatabaseEntry, IDatabaseEntryID
        {
            ApplyAdminInfo(entity, adminInfoChange);
            entity.date_Update = DateTime.Now;

            if (entity.id == 0)
            {
                entity.date_Create = DateTime.Now;
                SetGUID(entity);

                dbSet.Add(entity);
            }
            else
            {
                try { dbSet.Update(entity); }
                catch
                {
                    dbSet.Attach(entity);
                    dbSet.Entry(entity).State = EntityState.Modified;
                }
            }

            await _dbContext.SaveChangesAsync();
            return entity;
        }
        #endregion

        #region Add/Update (List)
        protected async Task AddOrUpdateListAsync<T>(DbSet<T> dbSet, IEnumerable<T> entities, IUserInfo? adminInfoChange = null)
            where T : class, IDatabaseEntry, IDatabaseEntryID
        {
            var entityList = entities.ToList();
            foreach (var entity in entityList)
            {
                ApplyAdminInfo(entity, adminInfoChange);
                entity.date_Update = DateTime.Now;

                if (entity.id == 0)
                {
                    SetGUID(entity);
                    entity.date_Create = DateTime.Now;
                }
            }

            var toAdd = entityList.Where(e => e.id == 0).ToList();
            var toUpdate = entityList.Where(e => e.id != 0).ToList();

            if (toAdd.Any())
            {
                _dbContext.AddRange(toAdd);
                await _dbContext.SaveChangesAsync();
            }
            if (toUpdate.Any())
            {
                _dbContext.UpdateRange(toUpdate);
                await _dbContext.SaveChangesAsync();
            }
        }
        #endregion

        #region Add/Update/Delete (List)
        protected async Task AddUpdateDeleteListAsync<T>(
            DbSet<T> dbSet,
            IEnumerable<T> newEntities,
            IEnumerable<T> oldEntities,
            IUserInfo? adminInfoChange = null)
            where T : class, IDatabaseEntry, IDatabaseEntryID
        {
            var newList = newEntities.ToList();
            var oldList = oldEntities.ToList();

            foreach (var entity in newList)
            {
                ApplyAdminInfo(entity, adminInfoChange);
                entity.date_Update = DateTime.Now;

                if (entity.id == 0)
                {
                    SetGUID(entity);
                    entity.date_Create = DateTime.Now;
                }
            }

            var toAdd = newList.Where(e => e.id == 0).ToList();
            var toUpdate = newList.Where(e => e.id != 0).ToList();
            var toDelete = oldList.Where(o => newList.All(n => n.id != o.id)).ToList();

            if (toAdd.Any()) _dbContext.AddRange(toAdd);
            if (toUpdate.Any()) _dbContext.UpdateRange(toUpdate);
            if (toDelete.Any()) _dbContext.RemoveRange(toDelete);

            await _dbContext.SaveChangesAsync();
        }
        #endregion

        #region Pagination
        public IQueryable<T> Paginate<T>(IQueryable<T> query, int pageIndex, int pageSize)
        {
            return query.Skip((pageIndex - 1) * pageSize).Take(pageSize);
        }
        #endregion

        #region Change Activation
        public async Task ChangeActivation<T>(
            DbSet<T> dbSet,
            int id,
            bool isActive,
            IUserInfo? adminInfoChange)
            where T : class, IDatabaseEntry, IDatabaseEntryID, IDatabaseEntryActivity
        {
            var dm = await dbSet
                .Where(q => q.id == id)
                .FirstOrDefaultAsync();

            if (dm is null)
                return;

            dm.isActive = isActive;
            await AddOrUpdateAsync(dbSet, dm, adminInfoChange);
        }
        #endregion

        #region Set GUID
        private static void SetGUID<T>(T entity) where T : class, IDatabaseEntry
        {
            // Use reflection to find a Guid property
            var guidProperty = typeof(T)
                .GetProperties()
                .FirstOrDefault(p => p.PropertyType == typeof(Guid) &&
                                     p.CanWrite);

            // If a Guid property is found, set it to a new Guid
            if (guidProperty != null && guidProperty.GetValue(entity)!.ToString() == Guid.Empty.ToString())
            { guidProperty.SetValue(entity, Guid.NewGuid()); }
        }
        #endregion

        #region Helpers
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
}
