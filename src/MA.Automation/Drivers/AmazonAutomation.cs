using MA.Core.Entities;
using MA.Core.Enums;
using MA.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace MA.Automation.Drivers;

/// <summary>
/// Amazon driver. Recommended path for production: Amazon SP-API (Selling Partner API)
/// rather than Seller Central browser automation - SP-API is officially supported,
/// gets you proper feed submission for listings/inventory/orders, and won't trip MFA.
///
/// This stub keeps the same IMarketplaceAutomation surface so the rest of the system
/// is identical. Internally, an SP-API HttpClient implementation goes here.
///
/// Browser fallback (PerformLoginAsync) is provided for portals SP-API doesn't cover.
/// </summary>
public class AmazonAutomation : PlaywrightMarketplaceBase
{
    public override Marketplace Marketplace => Marketplace.Amazon;
    protected override string LoginUrl => "https://sellercentral.amazon.in";

    private readonly ICredentialProtector _protector;

    public AmazonAutomation(ILogger<AmazonAutomation> logger, ICredentialProtector protector)
        : base(logger)
    {
        _protector = protector;
    }

    protected override async Task<bool> PerformLoginAsync(MarketplaceAccount account, CancellationToken ct)
    {
        // [VERIFY] - Seller Central has frequent A/B tests. Inspect before relying on these.
        var username = _protector.Decrypt(account.EncryptedUserName);
        var password = _protector.Decrypt(account.EncryptedPassword);

        await Page!.FillAsync("#ap_email", username);
        await Page.ClickAsync("#continue");
        await Page.FillAsync("#ap_password", password);
        await Page.ClickAsync("#signInSubmit");

        try
        {
            // Either MFA prompt or dashboard
            await Page.WaitForSelectorAsync("input[name='otpCode'], #partner-switcher",
                new() { Timeout = 30_000 });
        }
        catch (TimeoutException)
        {
            Logger.LogWarning("Amazon: login outcome page did not appear");
            return false;
        }

        var otp = await Page.QuerySelectorAsync("input[name='otpCode']");
        if (otp is not null)
        {
            Logger.LogInformation("Amazon MFA required - waiting up to 2 minutes for manual entry");
            await Page.WaitForSelectorAsync("#partner-switcher", new() { Timeout = 120_000 });
        }

        return true;
    }

    public override async Task<MarketplaceListingResult> CreateListingAsync(
        MarketplaceAccount account, Product product,
        IReadOnlyList<ProductImage> images, CancellationToken ct = default)
    {
        // TODO production: submit a JSON_LISTINGS_FEED via SP-API
        //   POST /feeds/2021-06-30/feeds with feedType=JSON_LISTINGS_FEED
        //   Body references the product's category & attributes.
        // Returns ASIN after feed processing succeeds.
        Logger.LogWarning("Amazon CreateListing stub - prefer SP-API JSON_LISTINGS_FEED for {SKU}", product.SKU);
        await Task.Delay(100, ct);
        return new MarketplaceListingResult(false, null, product.SKU,
            "Not implemented - use SP-API JSON_LISTINGS_FEED");
    }

    public override async Task<bool> PushInventoryAsync(
        MarketplaceAccount account, MarketplaceMapping mapping,
        int quantity, decimal price, CancellationToken ct = default)
    {
        // TODO: SP-API PATCH /listings/2021-08-01/items/{sellerId}/{sku}
        // patches['quantity', 'purchasable_offer'] in one call.
        Logger.LogWarning("Amazon PushInventory stub for {SKU}", mapping.ExternalSKU);
        await Task.Delay(100, ct);
        return false;
    }

    public override async Task<IReadOnlyList<RemoteOrder>> FetchOrdersAsync(
        MarketplaceAccount account, DateTime sinceUtc, CancellationToken ct = default)
    {
        // TODO: SP-API GET /orders/v0/orders?CreatedAfter=...&MarketplaceIds=A21TJRUUN4KGV
        Logger.LogWarning("Amazon FetchOrders stub since {Since}", sinceUtc);
        await Task.Delay(100, ct);
        return Array.Empty<RemoteOrder>();
    }
}
