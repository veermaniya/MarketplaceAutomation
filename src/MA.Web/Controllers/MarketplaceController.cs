using System.Security.Claims;
using MA.Core.Dtos;
using MA.Core.Entities;
using MA.Core.Enums;
using MA.Core.Interfaces;
using MA.Data;
using MA.Jobs;
using MA.Web.Models;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MA.Web.Controllers;

[Authorize]
public class MarketplaceAccountsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ICredentialProtector _protector;

    public MarketplaceAccountsController(AppDbContext db, ICredentialProtector protector)
    {
        _db = db; _protector = protector;
    }

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

    public async Task<IActionResult> Index()
    {
        var items = await _db.MarketplaceAccounts
            .Where(a => a.UserId == CurrentUserId)
            .OrderBy(a => a.Marketplace).ThenBy(a => a.DisplayName)
            .ToListAsync();
        return View(items);
    }

    [HttpGet] public IActionResult Create() => View("Edit", new MarketplaceAccountViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(MarketplaceAccountViewModel vm)
    {
        if (!ModelState.IsValid) return View("Edit", vm);

        MarketplaceAccount acc;
        if (vm.AccountId == 0)
        {
            acc = new MarketplaceAccount { UserId = CurrentUserId };
            _db.MarketplaceAccounts.Add(acc);
        }
        else
        {
            acc = await _db.MarketplaceAccounts.FindAsync(vm.AccountId)
                  ?? throw new InvalidOperationException("Account not found");
            if (acc.UserId != CurrentUserId) return Forbid();
        }

        acc.Marketplace        = vm.Marketplace;
        acc.DisplayName        = vm.DisplayName;
        acc.EncryptedUserName  = _protector.Encrypt(vm.UserName);
        acc.EncryptedPassword  = _protector.Encrypt(vm.Password);
        acc.EncryptedApiKey    = string.IsNullOrEmpty(vm.ApiKey)    ? null : _protector.Encrypt(vm.ApiKey);
        acc.EncryptedApiSecret = string.IsNullOrEmpty(vm.ApiSecret) ? null : _protector.Encrypt(vm.ApiSecret);
        acc.SellerId           = vm.SellerId;

        await _db.SaveChangesAsync();
        TempData["msg"] = $"Saved {acc.Marketplace} account '{acc.DisplayName}'";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var acc = await _db.MarketplaceAccounts.FindAsync(id);
        if (acc is null || acc.UserId != CurrentUserId) return NotFound();
        _db.MarketplaceAccounts.Remove(acc);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}

[Authorize]
public class MappingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IBackgroundJobClient _hangfire;

    public MappingsController(AppDbContext db, IBackgroundJobClient hangfire)
    {
        _db = db; _hangfire = hangfire;
    }

    public async Task<IActionResult> Index(int productId)
    {
        var product = await _db.Products
            .Include(p => p.Mappings).ThenInclude(m => m.Account)
            .Include(p => p.Mappings).ThenInclude(m => m.Inventory)
            .FirstOrDefaultAsync(p => p.ProductId == productId);

        if (product is null) return NotFound();
        ViewBag.Product = product;
        ViewBag.Accounts = await _db.MarketplaceAccounts
            .Where(a => a.IsActive)
            .OrderBy(a => a.Marketplace).ToListAsync();
        return View(product.Mappings.ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int productId, int accountId)
    {
        var account = await _db.MarketplaceAccounts.FindAsync(accountId);
        if (account is null) return NotFound();

        // Unique constraint will throw on duplicate
        var mapping = new MarketplaceMapping
        {
            ProductId   = productId,
            AccountId   = accountId,
            Marketplace = account.Marketplace,
            Status      = nameof(MappingStatus.Pending)
        };
        _db.MarketplaceMappings.Add(mapping);
        await _db.SaveChangesAsync();

        // Queue the listing job
        _hangfire.Enqueue<MarketplaceJobs>(j => j.CreateListingAsync(new ListingJobPayload
        {
            MappingId   = mapping.MappingId,
            ProductId   = productId,
            AccountId   = accountId,
            Marketplace = account.Marketplace
        }));

        TempData["msg"] = $"Mapping queued for {account.Marketplace}";
        return RedirectToAction(nameof(Index), new { productId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PushInventory(int mappingId, int quantity, decimal price)
    {
        var mapping = await _db.MarketplaceMappings.FindAsync(mappingId);
        if (mapping is null) return NotFound();

        _hangfire.Enqueue<MarketplaceJobs>(j => j.PushInventoryAsync(new InventoryPushJobPayload
        {
            MappingId = mappingId,
            Quantity  = quantity,
            Price     = price
        }));

        TempData["msg"] = "Inventory push queued";
        return RedirectToAction(nameof(Index), new { productId = mapping.ProductId });
    }
}
