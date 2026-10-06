using Microsoft.EntityFrameworkCore;
using SistemiPos.Application.Customers;
using SistemiPos.Domain.Configuration;
using SistemiPos.Infrastructure.Persistence;

namespace SistemiPos.Infrastructure.Customers;

public sealed class NitLookupConfigurationProvider(AppDbContext dbContext) : INitLookupConfigurationProvider
{
    public async Task<NitLookupProviderType> GetProviderTypeAsync(CancellationToken cancellationToken = default) =>
        await dbContext.SystemSettings
            .AsNoTracking()
            .Select(x => (NitLookupProviderType?)x.NitLookupProvider)
            .FirstOrDefaultAsync(cancellationToken)
        ?? NitLookupProviderType.None;
}
