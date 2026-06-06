using MA.Core.Enums;
using MA.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MA.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly AppDbContext _db;
    public DashboardController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Index()
    {
        ViewBag.ProductCount = await _db.Products.CountAsync();
        ViewBag.AccountCount = await _db.MarketplaceAccounts.CountAsync(a => a.IsActive);
        ViewBag.MappingCount = await _db.MarketplaceMappings.CountAsync();
        ViewBag.PendingRetries = await _db.RetryQueue.CountAsync(r => r.Status == nameof(RetryStatus.Pending));
        ViewBag.RecentLogs = await _db.AutomationLogs
            .OrderByDescending(l => l.OccurredOn).Take(20).ToListAsync();
        return View();
    }
}

public class HomeController : Controller
{
    public IActionResult Index() => RedirectToAction("Index", "Dashboard");
    public IActionResult Error() => View();
}
