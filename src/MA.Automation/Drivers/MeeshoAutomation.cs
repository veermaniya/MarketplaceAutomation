using MA.Core.Entities;
using MA.Core.Enums;
using MA.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace MA.Automation.Drivers;

/// <summary>
/// Meesho Supplier Panel driver.
/// Meesho has a partner API; preferred for production. Browser fallback kept here
/// for actions the API doesn't expose.
/// </summary>
public class MeeshoAutomation : PlaywrightMarketplaceBase
{
    public override Marketplace Marketplace => Marketplace.Meesho;
    protected override string LoginUrl => "https://supplier.meesho.com";

    private readonly ICredentialProtector _protector;

    public MeeshoAutomation(ILogger<MeeshoAutomation> logger, ICredentialProtector protector)
        : base(logger)
    {
        _protector = protector;
    }

    protected override async Task<bool> PerformLoginAsync(MarketplaceAccount account, CancellationToken ct)
    {
        // [VERIFY] selectors
        var username = _protector.Decrypt(account.EncryptedUserName); // mobile no / email
        var password = _protector.Decrypt(account.EncryptedPassword);

        // Meesho's login is mobile OTP based by default. If password login is enabled
        // for the account, the form structure differs - check current portal.
        await Page!.FillAsync("input[type='tel'], input[name='email']", username);
        await Page.FillAsync("input[type='password']", password);
        await Page.ClickAsync("button[type='submit']");

        try
        {
            await Page.WaitForSelectorAsync("input[name='otp'], aside.sidebar",
                new() { Timeout = 30_000 });
        }
        catch (TimeoutException)
        {
            return false;
        }

        var otp = await Page.QuerySelectorAsync("input[name='otp']");
        if (otp is not null)
        {
            Logger.LogInformation("Meesho OTP required - waiting up to 2 minutes");
            await Page.WaitForSelectorAsync("aside.sidebar", new() { Timeout = 120_000 });
        }
        return true;
    }

    public override async Task<MarketplaceListingResult> CreateListingAsync(
        MarketplaceAccount account, Product product,
        IReadOnlyList<ProductImage> images, CancellationToken ct = default)
    {
        Logger.LogWarning("Meesho CreateListing stub for {SKU}", product.SKU);
        await Task.Delay(100, ct);
        return new MarketplaceListingResult(false, null, product.SKU,
            "Not implemented - upload catalog via Meesho Supplier Panel or partner API");
    }

    public override async Task<bool> PushInventoryAsync(
        MarketplaceAccount account, MarketplaceMapping mapping,
        int quantity, decimal price, CancellationToken ct = default)
    {
        Logger.LogWarning("Meesho PushInventory stub for {SKU}", mapping.ExternalSKU);
        await Task.Delay(100, ct);
        return false;
    }

    public override async Task<IReadOnlyList<RemoteOrder>> FetchOrdersAsync(
        MarketplaceAccount account, DateTime sinceUtc, CancellationToken ct = default)
    {
        Logger.LogWarning("Meesho FetchOrders stub since {Since}", sinceUtc);
        await Task.Delay(100, ct);
        return Array.Empty<RemoteOrder>();
    }
}
