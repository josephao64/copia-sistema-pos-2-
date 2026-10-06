using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemiPos.Application.Security;

namespace SistemiPos.Web.Controllers;

[Authorize(Policy = PermissionCodes.SecurityAuditView)]
public sealed class SecurityAuditController(ISecurityAuditService auditService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var logs = await auditService.GetRecentSecurityLogsAsync(100);
        return View(logs);
    }
}

