using Microsoft.Extensions.Options;

namespace SgccElectricityNet.Worker.Services.Publishing;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPublishingServices(
        this IServiceCollection services,
        IConfigurationSection publishingSection)
    {
        services.AddOptions();
        services.AddLogging();

        foreach (var serviceSection in publishingSection.GetChildren())
        {
            var type = serviceSection.GetValue<PublishingServiceType>("Type");
            switch (type)
            {
                case PublishingServiceType.Mqtt:
                    services.AddSingleton(Options.Create(serviceSection.Get<MqttPublishingOptions>()!));
                    services.AddSingleton<IPublishingService, MqttPublishingService>();
                    break;
                case PublishingServiceType.Database:
                    throw new NotImplementedException("Database publishing service is not implemented yet.");
            }
        }

        return services;
    }
}

public enum PublishingServiceType
{
    Mqtt,
    Database,
}
