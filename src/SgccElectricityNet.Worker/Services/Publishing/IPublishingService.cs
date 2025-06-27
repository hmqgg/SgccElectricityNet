using SgccElectricityNet.Worker.Models;

namespace SgccElectricityNet.Worker.Services.Publishing;

public interface IPublishingService
{
    Task PublishAsync(ElectricityData data, CancellationToken cancellationToken = default);

    Task PublishBatchAsync(IEnumerable<ElectricityData> data, CancellationToken cancellationToken = default);
}
