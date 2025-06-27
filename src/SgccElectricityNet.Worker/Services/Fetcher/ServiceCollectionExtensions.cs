using Microsoft.Extensions.Options;

namespace SgccElectricityNet.Worker.Services.Fetcher;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFetcherService(
        this IServiceCollection services,
        IConfigurationSection fetcherSection)
    {
        services.AddOptions();
        services.AddLogging();

        var type = fetcherSection.GetValue<FetcherServiceType>("Type");
        switch (type)
        {
            case FetcherServiceType.Playwright:
                services.AddSingleton(Options.Create(fetcherSection.Get<FetcherOptions>()!));
                services.AddScoped<IFetcherService, PlaywrightFetcherService>();
                services.AddScoped<PlaywrightBrowserFactory>();
                break;
        }

        return services;
    }
}

public enum FetcherServiceType
{
    Playwright,
}
