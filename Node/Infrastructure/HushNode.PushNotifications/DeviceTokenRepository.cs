using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushNode.PushNotifications.Models;

namespace HushNode.PushNotifications;

/// <summary>
/// Repository implementation for device token data access.
/// </summary>
public class DeviceTokenRepository : RepositoryBase<PushNotificationsDbContext>, IDeviceTokenRepository
{
    public async Task AddAsync(DeviceToken token) =>
        await Context.DeviceTokens.AddAsync(token);

    public async Task<IEnumerable<DeviceToken>> GetActiveTokensForUserAsync(string userId) =>
        await Context.DeviceTokens
            .Where(x => x.UserId == userId && x.IsActive)
            .ToListAsync();

    public async Task<DeviceToken?> GetByTokenAsync(string token) =>
        await Context.DeviceTokens
            .SingleOrDefaultAsync(x => x.Token == token);

    public async Task UpdateLastUsedAsync(string token, DateTime lastUsed) =>
        await Context.DeviceTokens
            .Where(x => x.Token == token)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.LastUsedAt, lastUsed));

    public async Task DeactivateTokenAsync(string token) =>
        await Context.DeviceTokens
            .Where(x => x.Token == token)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.IsActive, false));

    public async Task<IEnumerable<DeviceToken>> GetStaleTokensAsync(DateTime threshold) =>
        await Context.DeviceTokens
            .Where(x => x.IsActive && x.LastUsedAt < threshold)
            .ToListAsync();

    public Task UpdateAsync(DeviceToken token)
    {
        Context.DeviceTokens.Update(token);
        return Task.CompletedTask;
    }
}
