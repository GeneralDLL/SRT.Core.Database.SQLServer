using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace SRT.Core.Database.SQLServer.Cache.Domains
{
    public class CashBackup
    {
        public int id { get; set; }

        public string? category { get; set; }
        /// <summary>
        /// 0 for general; each service has its own Service ID from appsettings.json to separate cache data per service.
        /// Must have an index on this column for performance.
        /// </summary>
        public int serviceID { get; set; } = 0;

        /// <summary>
        /// Host process / app name that wrote the row (<see cref="AppDomain.CurrentDomain"/>.FriendlyName).
        /// </summary>
        public string? serviceName { get; set; } = AppDomain.CurrentDomain.FriendlyName;

        /// <summary>
        /// Identifies the cache entry; must be unique for each (serviceID, category).
        /// Must have a regular index and a full-text index for search performance.
        /// </summary>
        public string? key { get; set; }

        public string? value1 { get; set; }
        public string? value2 { get; set; }
        public string? value3 { get; set; }

        public DateTime dateCreate { get; set; } = DateTime.Now;
        public DateTime dateExpire { get; set; } = DateTime.Now.AddDays(1);

        #region Combine Properties
        [JsonIgnore]
        [NotMapped]
        public string value
        {
            get => (value1 ?? "") + (value2 ?? "") + (value3 ?? "");
            set
            {
                value1 = value2 = value3 = null;
                if (string.IsNullOrEmpty(value))
                    return;

                const int chunkSize = 3990;
                var remaining = value;

                var len1 = Math.Min(remaining.Length, chunkSize);
                value1 = remaining.Substring(0, len1);
                remaining = remaining.Substring(len1);
                if (remaining.Length == 0)
                    return;

                var len2 = Math.Min(remaining.Length, chunkSize);
                value2 = remaining.Substring(0, len2);
                remaining = remaining.Substring(len2);
                if (remaining.Length == 0)
                    return;

                value3 = remaining;
            }
        }
        #endregion
    }
}
