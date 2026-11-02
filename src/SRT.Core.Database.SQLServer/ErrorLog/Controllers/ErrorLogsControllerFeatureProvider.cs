using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace SRT.Core.Database.SQLServer.ErrorLog.Controllers
{
    /// <summary>Exposes the error log viewer on every service whose error log store is SQL Server.</summary>
    internal sealed class ErrorLogsControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var controller = typeof(SRTErrorLogsController).GetTypeInfo();
            if (!feature.Controllers.Contains(controller))
                feature.Controllers.Add(controller);
        }
    }
}
