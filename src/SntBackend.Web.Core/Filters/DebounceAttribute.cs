using System;

namespace SntBackend.Web.Core.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class DebounceAttribute : Attribute
    {
    }
}
