using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using SRT.Core.Database.SQLServer.Cache.Domains;
using SRT.Core.Domain;
using SRT.Core.Interfaces.Cache;

namespace SRT.Core.Database.SQLServer.Cache
{
    /// <summary>
    /// SQL-only <see cref="CashBackup"/> store. Redis dual-store order lives in RedisSqlCacheRepository.
    /// Registered as keyed <see cref="ICacheRepository"/> with key "sql".
    /// </summary>
    public class SqlBackedCacheRepository : ICacheRepository
    {
        private readonly CacheDBContext _dbContext;
        private readonly int _serviceId;
        private readonly string _serviceName;

        public SqlBackedCacheRepository(CacheDBContext dbContext, AppSetting config)
        {
            _dbContext = dbContext;
            _serviceId = config.ServiceId;
            _serviceName = string.IsNullOrWhiteSpace(config.ServiceName)
                ? AppDomain.CurrentDomain.FriendlyName
                : config.ServiceName.Trim();
        }

        public async Task AddNewData(string category, string key, object value, DateTime expire)
        {
            await DeleteExpiredData();
            await UpsertBackupAsync(category, key, JsonConvert.SerializeObject(value), expire);
        }

        public async Task AddPersistentData(string category, string key, object value)
        {
            await DeleteExpiredData();
            await UpsertBackupAsync(category, key, JsonConvert.SerializeObject(value), DateTime.MaxValue);
        }

        public async Task<object?> GetData(string category, string key)
        {
            await DeleteExpiredData();

            var dmSQL = await FindBackupAsync(category, key);
            if (dmSQL == null || dmSQL.dateExpire < DateTime.Now)
                return null;

            return JsonConvert.DeserializeObject(dmSQL.value)!;
        }

        public async Task<T?> GetData<T>(string category, string key) where T : class, new()
        {
            await DeleteExpiredData();

            var dmSQL = await FindBackupAsync(category, key);
            if (dmSQL == null || dmSQL.dateExpire < DateTime.Now)
                return null;

            return JsonConvert.DeserializeObject<T>(dmSQL.value);
        }

        public async Task<bool> RemoveData(string category, string key)
        {
            await DeleteExpiredData();

            var dmSQL = await FindBackupAsync(category, key);
            if (dmSQL == null)
                return false;

            _dbContext.CashBackup.Remove(dmSQL);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<long> ClearCategory(string category)
        {
            await DeleteExpiredData();
            return await _dbContext.CashBackup
                .Where(x => x.serviceID == _serviceId && x.category == category)
                .ExecuteDeleteAsync();
        }

        public Task<long> GetCounterAsync(string category, string key)
            => throw RedisOnly();

        public Task<long> IncrementWithExpiryAsync(string category, string key, TimeSpan ttl)
            => throw RedisOnly();

        public Task<(bool Allowed, long Count)> TryIncrementIfBelowLimitAsync(string category, string key, long maxCount, TimeSpan ttl)
            => throw RedisOnly();

        public Task<long> GetAccountBackoffRemainingSecondsAsync(string category, string key, long nowUnixSeconds)
            => throw RedisOnly();

        public Task ApplyAccountBackoffAsync(string category, string key, long nowUnixSeconds, TimeSpan stateTtl, int backoffBaseSeconds, int backoffMaxSeconds)
            => throw RedisOnly();

        private Task<CashBackup?> FindBackupAsync(string category, string key)
            => _dbContext.CashBackup.FirstOrDefaultAsync(x =>
                x.serviceID == _serviceId && x.category == category && x.key == key);

        private async Task UpsertBackupAsync(string category, string key, string serialized, DateTime expire)
        {
            var serviceName = _serviceName;
            var existing = await FindBackupAsync(category, key);
            if (existing != null)
            {
                existing.serviceName = serviceName;
                existing.value = serialized;
                existing.dateExpire = expire;
                await _dbContext.SaveChangesAsync();
                return;
            }

            _dbContext.CashBackup.Add(new CashBackup
            {
                serviceID = _serviceId,
                serviceName = serviceName,
                category = category,
                key = key,
                value = serialized,
                dateExpire = expire,
            });

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                _dbContext.ChangeTracker.Clear();
                var row = await FindBackupAsync(category, key)
                    ?? throw new InvalidOperationException(
                        $"CashBackup upsert failed for serviceID={_serviceId}, category={category}, key={key}, and no row was found to retry.");

                row.serviceName = serviceName;
                row.value = serialized;
                row.dateExpire = expire;
                await _dbContext.SaveChangesAsync();
            }
        }

        private async Task DeleteExpiredData()
        {
            await _dbContext.CashBackup
                .Where(x => x.dateExpire < DateTime.Now)
                .ExecuteDeleteAsync();
        }

        private static NotSupportedException RedisOnly()
            => new("Counters, rate-limit, and backoff are Redis-only and are not stored in SQL CashBackup.");
    }
}
