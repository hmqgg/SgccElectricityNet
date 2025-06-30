using Coravel.Invocable;
using SgccElectricityNet.Worker.Services.Fetcher;
using SgccElectricityNet.Worker.Services.Publishing;

namespace SgccElectricityNet.Worker.Invocables;

public sealed class UpdateInvocable(
    IFetcherService fetcherService,
    IEnumerable<IPublishingService> publishingServices,
    ILogger<UpdateInvocable> logger) : IInvocable, ICancellableInvocable
{
    public CancellationToken CancellationToken { get; set; }
    public async Task Invoke()
    {
        logger.LogInformation("Operation started at: {time}", DateTimeOffset.Now);

        try
        {
            var records = await fetcherService.FetchAllAsync(CancellationToken);
            foreach (var publishingService in publishingServices)
            {
                await publishingService.PublishBatchAsync(records, CancellationToken);
            }
        }
        catch (InvalidOperationException ioe)
        {
            logger.LogError(ioe, "Operation failed at: {time}", DateTimeOffset.Now);
            throw;
        }
        catch (OperationCanceledException oce)
        {
            logger.LogError(oce, "Operation was cancelled at: {time}", DateTimeOffset.Now);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while executing UpdateInvocable.");
            throw;
        }
    }
}
