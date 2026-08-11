using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace WMS.Authorization;

/// <summary>
/// Allows API-key endpoints to bypass cookie authorization while each action still validates X-API-Key.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ApiKeyAllowAnonymousAttribute : Attribute, IAllowAnonymous, IAllowAnonymousFilter
{
}
