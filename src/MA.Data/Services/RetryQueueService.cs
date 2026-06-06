using System.Text.Json;
using MA.Core.Entities;
using MA.Core.Enums;
using MA.Core.Interfaces;

namespace MA.Data.Services;

public class RetryQueueService : IRetryQueueService
{
    private readonly AppDbContext _db;
    public RetryQueueService(AppDbContext db) { _db = db; }

    public async Task EnqueueAsync(string jobType, object payload, int maxAttempts = 5)
    {
        var item = new RetryQueueItem
        {
            JobType       = jobType,
            Payload       = JsonSerializer.Serialize(payload),
            MaxAttempts   = maxAttempts,
            NextAttemptOn = DateTime.UtcNow,
            Status        = nameof(RetryStatus.Pending)
        };
        _db.RetryQueue.Add(item);
        await _db.SaveChangesAsync();
    }

    public async Task MarkCompleteAsync(long queueId)
    {
        var item = await _db.RetryQueue.FindAsync(queueId);
        if (item is null) return;
        item.Status    = nameof(RetryStatus.Completed);
        item.UpdatedOn = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task MarkFailedAsync(long queueId, string error, TimeSpan backoff)
    {
        var item = await _db.RetryQueue.FindAsync(queueId);
        if (item is null) return;

        item.LastError = error;
        item.UpdatedOn = DateTime.UtcNow;

        if (item.AttemptCount >= item.MaxAttempts)
        {
            item.Status = nameof(RetryStatus.Dead);
        }
        else
        {
            item.Status        = nameof(RetryStatus.Pending);
            item.NextAttemptOn = DateTime.UtcNow.Add(backoff);
        }
        await _db.SaveChangesAsync();
    }
}
