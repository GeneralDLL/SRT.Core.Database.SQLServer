using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace SRT.Core.Database.SQLServer.ErrorLog.Domain
{
    public partial class ErrorReport
    {
        #region Main Properties
        public long id { get; set; }

        /// <summary>
        /// Legacy category (log/warning/error, etc.). Used for filtering; must have an index.
        /// </summary>
        public string? category { get; set; }

        /// <summary>
        /// 0 for general; each service can have its own Service ID from appsettings.
        /// Must have an index on this column for performance.
        /// </summary>
        public int serviceID { get; set; } = 0;

        /// <summary>
        /// Host process / app name that wrote the row (<see cref="AppDomain.CurrentDomain"/>.FriendlyName).
        /// </summary>
        public string? serviceName { get; set; } = AppDomain.CurrentDomain.FriendlyName;

        public DateTime? date { get; set; }

        public string? Message1 { get; set; }
        public string? Message2 { get; set; }
        public string? Message3 { get; set; }
        public string? Message4 { get; set; }
        public string? Message5 { get; set; }

        public string? StackTrace1 { get; set; }
        public string? StackTrace2 { get; set; }
        public string? StackTrace3 { get; set; }
        public string? StackTrace4 { get; set; }
        public string? StackTrace5 { get; set; }

        public string? layer1 { get; set; }
        public string? layer2 { get; set; }
        public string? layer3 { get; set; }
        public string? layer4 { get; set; }
        public string? layer5 { get; set; }
        public string? layer6 { get; set; }
        public string? layer7 { get; set; }
        public string? layer8 { get; set; }
        public string? layer9 { get; set; }
        public string? layer10 { get; set; }
        public string? layer11 { get; set; }
        public string? layer12 { get; set; }
        public string? layer13 { get; set; }
        public string? layer14 { get; set; }
        public string? layer15 { get; set; }
        public string? layer16 { get; set; }
        public string? layer17 { get; set; }
        public string? layer18 { get; set; }
        public string? layer19 { get; set; }
        public string? layer20 { get; set; }

        // Diagnostic envelope (Production triage) — indexed columns + DetailJson
        public Guid? ErrorId { get; set; }
        public string? CorrelationId { get; set; }
        public string? Service { get; set; }
        public string? Environment { get; set; }
        public string? Version { get; set; }
        public string? Category { get; set; }
        public string? Operation { get; set; }
        public string? Severity { get; set; }
        public string? Outcome { get; set; }
        public string? ExceptionType { get; set; }
        public string? Target { get; set; }
        public string? DetailJson { get; set; }
        #endregion

        #region Combine Properties
        private const int ChunkSize = 3990;

        [JsonIgnore]
        [NotMapped]
        public string? Message
        {
            get => (Message1 ?? "") + (Message2 ?? "") + (Message3 ?? "") + (Message4 ?? "") + (Message5 ?? "");
            set
            {
                var chunks = SplitChunks(value, 5);
                Message1 = chunks[0];
                Message2 = chunks[1];
                Message3 = chunks[2];
                Message4 = chunks[3];
                Message5 = chunks[4];
            }
        }

        [JsonIgnore]
        [NotMapped]
        public string? StackTrace
        {
            get => (StackTrace1 ?? "") + (StackTrace2 ?? "") + (StackTrace3 ?? "") + (StackTrace4 ?? "") + (StackTrace5 ?? "");
            set
            {
                var chunks = SplitChunks(value, 5);
                StackTrace1 = chunks[0];
                StackTrace2 = chunks[1];
                StackTrace3 = chunks[2];
                StackTrace4 = chunks[3];
                StackTrace5 = chunks[4];
            }
        }

        [JsonIgnore]
        [NotMapped]
        public string? layer
        {
            get =>
                (layer1 ?? "") + (layer2 ?? "") + (layer3 ?? "") + (layer4 ?? "") + (layer5 ?? "") +
                (layer6 ?? "") + (layer7 ?? "") + (layer8 ?? "") + (layer9 ?? "") + (layer10 ?? "") +
                (layer11 ?? "") + (layer12 ?? "") + (layer13 ?? "") + (layer14 ?? "") + (layer15 ?? "") +
                (layer16 ?? "") + (layer17 ?? "") + (layer18 ?? "") + (layer19 ?? "") + (layer20 ?? "");
            set
            {
                var chunks = SplitChunks(value, 20);
                layer1 = chunks[0];
                layer2 = chunks[1];
                layer3 = chunks[2];
                layer4 = chunks[3];
                layer5 = chunks[4];
                layer6 = chunks[5];
                layer7 = chunks[6];
                layer8 = chunks[7];
                layer9 = chunks[8];
                layer10 = chunks[9];
                layer11 = chunks[10];
                layer12 = chunks[11];
                layer13 = chunks[12];
                layer14 = chunks[13];
                layer15 = chunks[14];
                layer16 = chunks[15];
                layer17 = chunks[16];
                layer18 = chunks[17];
                layer19 = chunks[18];
                layer20 = chunks[19];
            }
        }

        private static string?[] SplitChunks(string? value, int count)
        {
            var chunks = new string?[count];
            if (string.IsNullOrWhiteSpace(value))
                return chunks;

            var remaining = value;
            for (var i = 0; i < count; i++)
            {
                if (remaining.Length == 0)
                    break;

                if (i == count - 1)
                {
                    chunks[i] = remaining;
                    break;
                }

                var len = Math.Min(remaining.Length, ChunkSize);
                chunks[i] = remaining.Substring(0, len);
                remaining = remaining.Substring(len);
            }

            return chunks;
        }
        #endregion

        #region New Instance
        public static ErrorReport New_Instance(ErrorReport item)
        {
            return new ErrorReport
            {
                id = item.id,
                date = item.date,
                Message1 = item.Message1,
                Message2 = item.Message2,
                Message3 = item.Message3,
                Message4 = item.Message4,
                Message5 = item.Message5,
                StackTrace1 = item.StackTrace1,
                StackTrace2 = item.StackTrace2,
                StackTrace3 = item.StackTrace3,
                StackTrace4 = item.StackTrace4,
                StackTrace5 = item.StackTrace5,
                layer1 = item.layer1,
                layer2 = item.layer2,
                layer3 = item.layer3,
                layer4 = item.layer4,
                layer5 = item.layer5,
                layer6 = item.layer6,
                layer7 = item.layer7,
                layer8 = item.layer8,
                layer9 = item.layer9,
                layer10 = item.layer10,
                layer11 = item.layer11,
                layer12 = item.layer12,
                layer13 = item.layer13,
                layer14 = item.layer14,
                layer15 = item.layer15,
                layer16 = item.layer16,
                layer17 = item.layer17,
                layer18 = item.layer18,
                layer19 = item.layer19,
                layer20 = item.layer20,
                category = item.category,
                serviceID = item.serviceID,
                serviceName = item.serviceName,
                ErrorId = item.ErrorId,
                CorrelationId = item.CorrelationId,
                Service = item.Service,
                Environment = item.Environment,
                Version = item.Version,
                Category = item.Category,
                Operation = item.Operation,
                Severity = item.Severity,
                Outcome = item.Outcome,
                ExceptionType = item.ExceptionType,
                Target = item.Target,
                DetailJson = item.DetailJson,
            };
        }

        public ErrorReport NewInstance()
            => New_Instance(this);
        #endregion

        public override bool Equals(object? obj)
        {
            try
            {
                if (obj is not ErrorReport data2)
                    return false;

                return
                    id == data2.id &&
                    Message1 == data2.Message1 &&
                    Message2 == data2.Message2 &&
                    Message3 == data2.Message3 &&
                    Message4 == data2.Message4 &&
                    Message5 == data2.Message5 &&
                    StackTrace1 == data2.StackTrace1 &&
                    StackTrace2 == data2.StackTrace2 &&
                    StackTrace3 == data2.StackTrace3 &&
                    StackTrace4 == data2.StackTrace4 &&
                    StackTrace5 == data2.StackTrace5 &&
                    layer1 == data2.layer1 &&
                    layer2 == data2.layer2 &&
                    layer3 == data2.layer3 &&
                    layer4 == data2.layer4 &&
                    layer5 == data2.layer5 &&
                    layer6 == data2.layer6 &&
                    layer7 == data2.layer7 &&
                    layer8 == data2.layer8 &&
                    layer9 == data2.layer9 &&
                    layer10 == data2.layer10 &&
                    layer11 == data2.layer11 &&
                    layer12 == data2.layer12 &&
                    layer13 == data2.layer13 &&
                    layer14 == data2.layer14 &&
                    layer15 == data2.layer15 &&
                    layer16 == data2.layer16 &&
                    layer17 == data2.layer17 &&
                    layer18 == data2.layer18 &&
                    layer19 == data2.layer19 &&
                    layer20 == data2.layer20 &&
                    category == data2.category &&
                    serviceID == data2.serviceID &&
                    serviceName == data2.serviceName;
            }
            catch
            {
                return false;
            }
        }

        public override int GetHashCode()
            => base.GetHashCode();

        #region Operator
        public static bool operator ==(ErrorReport? data1, ErrorReport? data2)
        {
            if (data1 is null ^ data2 is null)
                return false;
            if (data1 is null && data2 is null)
                return true;

            return data1?.Equals(data2) ?? false;
        }

        public static bool operator !=(ErrorReport? data1, ErrorReport? data2)
            => !(data1 == data2);
        #endregion
    }
}
