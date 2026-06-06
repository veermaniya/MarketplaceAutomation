using System.Diagnostics;
using System.Text.Json;
using MA.Core.Dtos;
using MA.Core.Entities;
using MA.Core.Enums;
using MA.Core.Interfaces;
using MA.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MA.Jobs;

/// <summary>
/// All jobs follow the same shape:
///   1. Load entities, write 'Started' log
///   2. Call IMarketplaceAutomation driver
///   3. Write 'Success' or 'Failed' log with duration
///   4. On failure, enqueue retry with exponential backoff
/// </summary>
public class MarketplaceJobs
{
    private readonly AppDbContext _db;
    private readonly IMarketplaceAutomationFactory _factory;
    private readonly IRetryQueueService _retry;
    private readonly ILogger<MarketplaceJobs> _log;

    public MarketplaceJobs(
        AppDbContext db,
        IMarketplaceAutomationFactory factory,
        IRetryQueueService retry,
        ILogger<MarketplaceJobs> log)
    {
        _db = db;
        _factory = factory;
        _retry = retry;
        _log = log;
    }

    // -------------------------------------------------------------------
    // CREATE LISTING
    // -------------------------------------------------------------------
    public async Task CreateListingAsync(ListingJobPayload p)
    {
        var sw = Stopwatch.StartNew();
        var mapping = await _db.MarketplaceMappings
            .Include(m => m.Product).ThenInclude(p => p!.Images)
            .Include(m => m.Account)
            .FirstOrDefaultAsync(m => m.MappingId == p.MappingId);

        if (mapping?.Product is null || mapping.Account is null)
        {
            _log.LogError("Mapping {Id} not found", p.MappingId);
            return;
        }

        await WriteLogAsync(p.MappingId, mapping.Marketplace, "CreateListing", AutomationStatus.Started, null, null);

        try
        {
            var driver = _factory.Get(mapping.Marketplace);
            if (!await driver.LoginAsync(mapping.Account))
                throw new InvalidOperationException("Login failed");

            var result = await driver.CreateListingAsync(
                mapping.Account, mapping.Product,
                mapping.Product.Images.OrderBy(i => i.SortOrder).ToList());

            if (result.Success)
            {
                mapping.ExternalListingId = result.ExternalListingId;
                mapping.ExternalSKU       = result.ExternalSku;
                mapping.Status            = nameof(MappingStatus.Listed);
                mapping.LastSyncedOn      = DateTime.UtcNow;
                mapping.LastError         = null;
                await _db.SaveChangesAsync();
                await WriteLogAsync(p.MappingId, mapping.Marketplace, "CreateListing",
                    AutomationStatus.Success, $"Listed as {result.ExternalListingId}", (int)sw.ElapsedMilliseconds);
            }
            else
            {
                mapping.Status    = nameof(MappingStatus.Failed);
                mapping.LastError = result.ErrorMessage;
                await _db.SaveChangesAsync();
                throw new Exception(result.ErrorMessage ?? "Listing failed");
            }
        }
        catch (Exception ex)
        {
            await WriteLogAsync(p.MappingId, mapping.Marketplace, "CreateListing",
                AutomationStatus.Failed, ex.Message, (int)sw.ElapsedMilliseconds);
            await _retry.EnqueueAsync("CreateListing", p, maxAttempts: 5);
            throw;
        }
    }

    // -------------------------------------------------------------------
    // PUSH INVENTORY
    // -------------------------------------------------------------------
    public async Task PushInventoryAsync(InventoryPushJobPayload p)
    {
        var sw = Stopwatch.StartNew();
        var mapping = await _db.MarketplaceMappings
            .Include(m => m.Account)
            .Include(m => m.Inventory)
            .FirstOrDefaultAsync(m => m.MappingId == p.MappingId);

        if (mapping?.Account is null) return;

        await WriteLogAsync(p.MappingId, mapping.Marketplace, "PushInventory", AutomationStatus.Started, null, null);

        try
        {
            var driver = _factory.Get(mapping.Marketplace);
            if (!await driver.LoginAsync(mapping.Account))
                throw new InvalidOperationException("Login failed");

            var ok = await driver.PushInventoryAsync(mapping.Account, mapping, p.Quantity, p.Price);
            if (!ok) throw new Exception("Driver returned false");

            if (mapping.Inventory is not null)
            {
                mapping.Inventory.AvailableQty = p.Quantity;
                mapping.Inventory.Price        = p.Price;
                mapping.Inventory.LastPushedOn = DateTime.UtcNow;
                mapping.Inventory.UpdatedOn    = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            await WriteLogAsync(p.MappingId, mapping.Marketplace, "PushInventory",
                AutomationStatus.Success, $"qty={p.Quantity} price={p.Price}", (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            await WriteLogAsync(p.MappingId, mapping.Marketplace, "PushInventory",
                AutomationStatus.Failed, ex.Message, (int)sw.ElapsedMilliseconds);
            await _retry.EnqueueAsync("PushInventory", p);
            throw;
        }
    }

    // -------------------------------------------------------------------
    // FETCH ORDERS (scheduled every 15 min via Hangfire RecurringJob)
    // -------------------------------------------------------------------
    public async Task FetchOrdersAsync(int accountId)
    {
        var sw = Stopwatch.StartNew();
        var account = await _db.MarketplaceAccounts.FindAsync(accountId);
        if (account is null || !account.IsActive) return;

        var since = account.LastUsedOn ?? DateTime.UtcNow.AddDays(-1);
        await WriteLogAsync(null, account.Marketplace, "FetchOrders", AutomationStatus.Started, $"since {since:o}", null, account.AccountId);

        try
        {
            var driver = _factory.Get(account.Marketplace);
            if (!await driver.LoginAsync(account))
                throw new InvalidOperationException("Login failed");

            var orders = await driver.FetchOrdersAsync(account, since);
            int newCount = 0;

            foreach (var o in orders)
            {
                bool exists = await _db.Orders.AnyAsync(x =>
                    x.Marketplace == account.Marketplace &&
                    x.MarketplaceOrderId == o.MarketplaceOrderId);
                if (exists) continue;

                _db.Orders.Add(new Order
                {
                    AccountId          = account.AccountId,
                    Marketplace        = account.Marketplace,
                    MarketplaceOrderId = o.MarketplaceOrderId,
                    OrderStatus        = o.Status,
                    OrderQty           = o.Qty,
                    OrderAmount        = o.Amount,
                    BuyerName          = o.BuyerName,
                    BuyerCity          = o.BuyerCity,
                    OrderedOn          = o.OrderedOn,
                    RawPayload         = o.RawPayload
                });
                newCount++;
            }

            account.LastUsedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await WriteLogAsync(null, account.Marketplace, "FetchOrders",
                AutomationStatus.Success, $"{newCount} new", (int)sw.ElapsedMilliseconds, account.AccountId);
        }
        catch (Exception ex)
        {
            await WriteLogAsync(null, account.Marketplace, "FetchOrders",
                AutomationStatus.Failed, ex.Message, (int)sw.ElapsedMilliseconds, account.AccountId);
            throw;
        }
    }

    // -------------------------------------------------------------------
    // RETRY QUEUE WORKER (runs every minute, picks up dead jobs)
    // -------------------------------------------------------------------
    public async Task ProcessRetryQueueAsync()
    {
        var due = await _db.RetryQueue
            .Where(r => r.Status == nameof(RetryStatus.Pending) && r.NextAttemptOn <= DateTime.UtcNow)
            .OrderBy(r => r.NextAttemptOn)
            .Take(20)
            .ToListAsync();

        foreach (var item in due)
        {
            item.Status = nameof(RetryStatus.InProgress);
            item.AttemptCount++;
            item.UpdatedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            try
            {
                switch (item.JobType)
                {
                    case "CreateListing":
                        await CreateListingAsync(JsonSerializer.Deserialize<ListingJobPayload>(item.Payload)!);
                        break;
                    case "PushInventory":
                        await PushInventoryAsync(JsonSerializer.Deserialize<InventoryPushJobPayload>(item.Payload)!);
                        break;
                    default:
                        _log.LogWarning("Unknown retry job type {Type}", item.JobType);
                        break;
                }
                await _retry.MarkCompleteAsync(item.QueueId);
            }
            catch (Exception ex)
            {
                // Exponential backoff: 1, 2, 4, 8, 16 minutes
                var backoff = TimeSpan.FromMinutes(Math.Pow(2, item.AttemptCount - 1));
                await _retry.MarkFailedAsync(item.QueueId, ex.Message, backoff);
            }
        }
    }

    // -------------------------------------------------------------------
    private async Task WriteLogAsync(int? mappingId, string marketplace, string action,
        AutomationStatus status, string? message, int? durationMs, int? accountId = null)
    {
        _db.AutomationLogs.Add(new AutomationLog
        {
            MappingId   = mappingId,
            AccountId   = accountId,
            Marketplace = marketplace,
            Action      = action,
            Status      = status.ToString(),
            Message     = message,
            DurationMs  = durationMs
        });
        await _db.SaveChangesAsync();
    }
}
