using SgccElectricityNet.Worker.Models;

namespace SgccElectricityNet.Worker.Services.Fetcher;

public interface IFetcherService
{
    Task<ElectricityData> FetchAsync(string userId, CancellationToken cancellationToken = default);

    Task<IEnumerable<ElectricityData>> FetchAllAsync(CancellationToken cancellationToken = default);
}
