using MA.Core.Entities;
using MA.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace MA.Automation.Drivers;

/// <summary>
/// Shared Playwright plumbing for all marketplace drivers.
/// Each driver gets its own browser context per session for cookie/storage isolation.
/// Section 7 Windows Automation Setup: Browser Profiles -> dedicated user data dir per account.
/// </summary>
public abstract class PlaywrightMarketplaceBase : IMarketplaceAutomation, IAsyncDisposable
{
    protected readonly ILogger Logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    protected IBrowserContext? Context;
    protected IPage? Page;

    public abstract MA.Core.Enums.Marketplace Marketplace { get; }
    protected abstract string LoginUrl { get; }

    /// <summary>
    /// Override per-marketplace. Returns true on a successful authenticated session.
    /// </summary>
    protected abstract Task<bool> PerformLoginAsync(MarketplaceAccount account, CancellationToken ct);

    public abstract Task<MarketplaceListingResult> CreateListingAsync(
        MarketplaceAccount account, Product product,
        IReadOnlyList<ProductImage> images, CancellationToken ct = default);

    public abstract Task<bool> PushInventoryAsync(
        MarketplaceAccount account, MarketplaceMapping mapping,
        int quantity, decimal price, CancellationToken ct = default);

    public abstract Task<IReadOnlyList<RemoteOrder>> FetchOrdersAsync(
        MarketplaceAccount account, DateTime sinceUtc, CancellationToken ct = default);

    protected PlaywrightMarketplaceBase(ILogger logger) { Logger = logger; }

    /// <summary>
    /// Public entry from IMarketplaceAutomation. Spins up Playwright once
    /// per session, ensures a persistent context per account.
    /// </summary>
    public async Task<bool> LoginAsync(MarketplaceAccount account, CancellationToken ct = default)
    {
        await EnsureBrowserAsync(account, ct);
        try
        {
            await Page!.GotoAsync(LoginUrl, new PageGotoOptions { Timeout = 45_000 });
            return await PerformLoginAsync(account, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{Market} login failed for account {AccountId}", Marketplace, account.AccountId);
            await TakeScreenshotAsync($"login_fail_{Marketplace}_{account.AccountId}");
            return false;
        }
    }

    private async Task EnsureBrowserAsync(MarketplaceAccount account, CancellationToken ct)
    {
        if (Context is not null) return;

        _playwright ??= await Playwright.CreateAsync();

        // Persistent context per account => session cookies survive between runs.
        // Browser profile path: %APPDATA%\MAAutomation\<marketplace>\<accountId>
        var profileRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MAAutomation",
            Marketplace.ToString(),
            account.AccountId.ToString());

        Directory.CreateDirectory(profileRoot);

        Context = await _playwright.Chromium.LaunchPersistentContextAsync(profileRoot, new()
        {
            Headless = false,   // many seller portals fingerprint headless; flip to true once stable
            ViewportSize = new() { Width = 1366, Height = 800 },
            Args = new[] { "--disable-blink-features=AutomationControlled" }
        });

        Page = Context.Pages.FirstOrDefault() ?? await Context.NewPageAsync();
    }

    protected async Task TakeScreenshotAsync(string label)
    {
        try
        {
            if (Page is null) return;
            var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{label}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png");
            await Page.ScreenshotAsync(new() { Path = path, FullPage = true });
            Logger.LogInformation("Screenshot saved: {Path}", path);
        }
        catch { /* best-effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (Context is not null) await Context.DisposeAsync();
        _browser?.DisposeAsync().GetAwaiter().GetResult();
        _playwright?.Dispose();
    }
}
