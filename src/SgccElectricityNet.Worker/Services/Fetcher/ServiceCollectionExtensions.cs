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
                var wsUrl = fetcherSection.GetValue<string>(nameof(PlaywrightBrowserFactoryOptions.WebSocket));
                var factoryOptions = new PlaywrightBrowserFactoryOptions(
                    IsRemote: !string.IsNullOrEmpty(wsUrl),
                    WebSocket: wsUrl);
                services.AddSingleton(Options.Create(fetcherSection.Get<FetcherOptions>()!));
                services.AddSingleton(Options.Create(factoryOptions));
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
