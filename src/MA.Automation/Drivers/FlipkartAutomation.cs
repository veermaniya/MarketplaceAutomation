using MA.Core.Entities;
using MA.Core.Enums;
using MA.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace MA.Automation.Drivers;

/// <summary>
/// Flipkart Seller Hub driver.
///
/// SELECTORS MARKED [VERIFY] CHANGE FREQUENTLY. When Flipkart updates their
/// portal, re-record with `npx playwright codegen seller.flipkart.com`.
/// </summary>
public class FlipkartAutomation : PlaywrightMarketplaceBase
{
    public override Marketplace Marketplace => Marketplace.Flipkart;
    protected override string LoginUrl => "https://seller.flipkart.com";

    private readonly ICredentialProtector _protector;

    public FlipkartAutomation(ILogger<FlipkartAutomation> logger, ICredentialProtector protector)
        : base(logger)
    {
        _protector = protector;
    }

    protected override async Task<bool> PerformLoginAsync(MarketplaceAccount account, CancellationToken ct)
    {
        var username = _protector.Decrypt(account.EncryptedUserName);
        var password = _protector.Decrypt(account.EncryptedPassword);

        // [VERIFY] Selectors current as of writing - inspect & update if portal changes.
        await Page!.FillAsync("input[name='username']", username);
        await Page.FillAsync("input[name='password']", password);
        await Page.ClickAsync("button[type='submit']");

        // Wait for dashboard nav or OTP screen
        try
        {
            await Page.WaitForSelectorAsync("nav.seller-nav, [data-testid='otp-input']",
                new() { Timeout = 30_000 });
        }
        catch (TimeoutException)
        {
            Logger.LogWarning("Flipkart: neither dashboard nor OTP appeared in 30s");
            return false;
        }

        // OTP path - operator must fill manually in non-headless window
        var otpInput = await Page.QuerySelectorAsync("[data-testid='otp-input']");
        if (otpInput is not null)
        {
            Logger.LogInformation("Flipkart OTP required - waiting up to 2 minutes for manual entry");
            await Page.WaitForSelectorAsync("nav.seller-nav", new() { Timeout = 120_000 });
        }

        return true;
    }

    public override async Task<MarketplaceListingResult> CreateListingAsync(
        MarketplaceAccount account, Product product,
        IReadOnlyList<ProductImage> images, CancellationToken ct = default)
    {
        // TODO: Real implementation requires:
        //   1. Navigate to /single-listing
        //   2. Map product.Category -> Flipkart category tree (cached lookup)
        //   3. Fill mandatory attributes, dimensions, MRP, selling price
        //   4. Upload images (file input via SetInputFilesAsync)
        //   5. Submit + capture FSN from confirmation page
        //
        // Returning a stub so the pipeline wires end-to-end.
        Logger.LogWarning("CreateListing stub - implement using Flipkart Listings UI for product {SKU}", product.SKU);
        await Task.Delay(100, ct);
        return new MarketplaceListingResult(
            Success: false,
            ExternalListingId: null,
            ExternalSku: product.SKU,
            ErrorMessage: "Not implemented - see TODO in FlipkartAutomation.CreateListingAsync");
    }

    public override async Task<bool> PushInventoryAsync(
        MarketplaceAccount account, MarketplaceMapping mapping,
        int quantity, decimal price, CancellationToken ct = default)
    {
        Logger.LogWarning("PushInventory stub for mapping {MappingId} qty={Qty} price={Price}",
            mapping.MappingId, quantity, price);
        await Task.Delay(100, ct);
        return false;
    }

    public override async Task<IReadOnlyList<RemoteOrder>> FetchOrdersAsync(
        MarketplaceAccount account, DateTime sinceUtc, CancellationToken ct = default)
    {
        Logger.LogWarning("FetchOrders stub - fetching since {Since}", sinceUtc);
        await Task.Delay(100, ct);
        return Array.Empty<RemoteOrder>();
    }
}
