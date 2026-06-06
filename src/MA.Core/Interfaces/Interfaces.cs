using MA.Core.Entities;
using MA.Core.Enums;

namespace MA.Core.Interfaces;

/// <summary>
/// Encrypts/decrypts marketplace credentials using ASP.NET Data Protection.
/// Implementation lives in MA.Web (where DataProtection is registered).
/// </summary>
public interface ICredentialProtector
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}

/// <summary>
/// Single marketplace driver - one implementation per marketplace.
/// Section 6: Login -> Create Listing -> Sync Inventory pattern.
/// </summary>
public interface IMarketplaceAutomation
{
    Marketplace Marketplace { get; }

    Task<bool> LoginAsync(MarketplaceAccount account, CancellationToken ct = default);

    Task<MarketplaceListingResult> CreateListingAsync(
        MarketplaceAccount account,
        Product product,
        IReadOnlyList<ProductImage> images,
        CancellationToken ct = default);

    Task<bool> PushInventoryAsync(
        MarketplaceAccount account,
        MarketplaceMapping mapping,
        int quantity,
        decimal price,
        CancellationToken ct = default);

    Task<IReadOnlyList<RemoteOrder>> FetchOrdersAsync(
        MarketplaceAccount account,
        DateTime sinceUtc,
        CancellationToken ct = default);
}

public record MarketplaceListingResult(
    bool Success,
    string? ExternalListingId,
    string? ExternalSku,
    string? ErrorMessage);

public record RemoteOrder(
    string MarketplaceOrderId,
    string Status,
    int Qty,
    decimal Amount,
    string? BuyerName,
    string? BuyerCity,
    DateTime OrderedOn,
    string? ExternalSku,
    string RawPayload);

/// <summary>
/// Resolves the right driver for a given marketplace at runtime.
/// </summary>
public interface IMarketplaceAutomationFactory
{
    IMarketplaceAutomation Get(Marketplace marketplace);
    IMarketplaceAutomation Get(string marketplaceName);
}

/// <summary>
/// Adds a job to the retry queue. Jobs persisted in dbo.RetryQueue.
/// </summary>
public interface IRetryQueueService
{
    Task EnqueueAsync(string jobType, object payload, int maxAttempts = 5);
    Task MarkCompleteAsync(long queueId);
    Task MarkFailedAsync(long queueId, string error, TimeSpan backoff);
}
