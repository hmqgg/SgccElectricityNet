using System.Text.Json;
using HomeAssistantDiscoveryNet;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Diagnostics.Logger;
using SgccElectricityNet.Worker.Models;

namespace SgccElectricityNet.Worker.Services.Publishing;

public sealed class MqttPublishingService : IPublishingService
{
    // TODO: inject repository URL instead.
    private const string Url = "https://github.com/hmqgg/SgccElectricityNet";
    private const string Manufacturer = nameof(SgccElectricityNet);
    private const string DateIcon = "mdi:calendar-range";
    private const string CurrencyIcon = "mdi:currency-cny";

    private static readonly HomeAssistantUnits CurrencyUnit = new("CNY");

    private readonly int _maxAttempts;
    private readonly MqttClientOptions _clientOptions;
    private readonly MqttClientFactory _mqttClientFactory;
    private readonly ILogger _logger;

    public MqttPublishingService(IOptions<MqttPublishingOptions> options, ILogger<MqttPublishingService> logger)
    {
        var clientOptionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId(nameof(SgccElectricityNet))
            .WithCleanStart()
            .WithTcpServer(options.Value.Host, options.Value.Port);
        if (!string.IsNullOrEmpty(options.Value.Username) && !string.IsNullOrEmpty(options.Value.Password))
        {
            clientOptionsBuilder = clientOptionsBuilder.WithCredentials(options.Value.Username, options.Value.Password);
        }
        _maxAttempts = options.Value.MaxAttempts;
        _clientOptions = clientOptionsBuilder.Build();
        _mqttClientFactory = new MqttClientFactory(new MqttLoggerForwarder(logger));
        _logger = logger;
    }

    public async Task PublishAsync(ElectricityData data, CancellationToken cancellationToken = default)
    {
        var discoveryDevice = GenerateDiscoveryDevice(data);
        var deviceId = discoveryDevice.Device.Identifiers!.First();
        var discoveryTopic = $"homeassistant/device/{deviceId}/config";
        var stateTopic = discoveryDevice.StateTopic;

        var discoveryPayload = JsonSerializer.Serialize(discoveryDevice, MqttDiscoveryJsonContext.Default.MqttDeviceDiscoveryConfig);
        var discoveryMessage = new MqttApplicationMessageBuilder()
            .WithTopic(discoveryTopic)
            .WithPayload(discoveryPayload)
            .Build();

        using var mqttClient = _mqttClientFactory.CreateMqttClient();
        await TryConnectWithRetryAsync(mqttClient, cancellationToken);
        // await mqttClient.ConnectAsync(_clientOptions, cancellationToken);

        await mqttClient.PublishAsync(discoveryMessage, cancellationToken);
        _logger.LogInformation("Published discovery config for user {UserId} with device ID {DeviceId}", data.UserId, deviceId);

        var statePayload = GenerateStatePayload(data);
        var stateMessage = new MqttApplicationMessageBuilder()
            .WithTopic(stateTopic)
            .WithPayload(JsonSerializer.Serialize(statePayload))
            .WithRetainFlag()
            .Build();

        await mqttClient.PublishAsync(stateMessage, cancellationToken);
        _logger.LogInformation("Published state for user {UserId} to topic {StateTopic}", data.UserId, stateTopic);
    }

    public async Task PublishBatchAsync(IEnumerable<ElectricityData> data, CancellationToken cancellationToken = default)
    {
        foreach (var electricityData in data)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Cancelled publishing batch states for user: {userId}", electricityData.UserId);
                return;
            }
            await PublishAsync(electricityData, cancellationToken);
        }
    }

    private async Task TryConnectWithRetryAsync(IMqttClient mqttClient, CancellationToken cancellationToken)
    {
        if (mqttClient.IsConnected)
        {
            return;
        }


        for (var i = 0; i < _maxAttempts; i++)
        {
            try
            {
                var result = await mqttClient.ConnectAsync(_clientOptions, cancellationToken);
                if (result.ResultCode == MqttClientConnectResultCode.Success)
                {
                    return;
                }

                _logger.LogError("MQTT connection failed with result: {code}", result.ResultCode);
                throw new Exception("MQTT connection failed with unsuccessful code.");
            }
            catch (Exception ex) when (ex.Message.Equals("MQTT connection failed with unsuccessful code."))
            {
                throw;
            }
            catch (Exception ex) when (i < _maxAttempts - 1)
            {
                _logger.LogWarning(ex, "MQTT connection failed, retrying in attempt: {i}", i + 1);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)), cancellationToken);
            }
        }
    }

    private static Dictionary<string, object> GenerateStatePayload(ElectricityData data)
    {
        var statePayload = new Dictionary<string, object>();

        if (data.Balance.HasValue)
        {
            statePayload["balance"] = data.Balance.Value;
        }
        if (data.LastUpdateTime.HasValue)
        {
            statePayload["last_update_time"] = data.LastUpdateTime.Value.ToString("yyyy-MM-dd");
        }
        if (data.LastDayUsage.HasValue)
        {
            statePayload["last_day_usage"] = data.LastDayUsage.Value;
        }
        if (data.CurrentYearCharge.HasValue)
        {
            statePayload["current_year_charge"] = data.CurrentYearCharge.Value;
        }
        if (data.CurrentYearUsage.HasValue)
        {
            statePayload["current_year_usage"] = data.CurrentYearUsage.Value;
        }
        if (data.LastMonthCharge.HasValue)
        {
            statePayload["last_month_charge"] = data.LastMonthCharge.Value;
        }
        if (data.LastMonthUsage.HasValue)
        {
            statePayload["last_month_usage"] = data.LastMonthUsage.Value;
        }

        // Add periodically reset timestamps (ISO 8601-formatted string).
        // For SGCC, the timezone should be UTC+8 only.
        var offset = TimeSpan.FromHours(+8);
        var now = DateTimeOffset.Now.ToOffset(offset);
        var yearStartsAt = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, offset);
        var monthStartsAt = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, offset);
        var dayStartsAt = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, offset);

        statePayload["last_reset_yearly"] = yearStartsAt.UtcDateTime.ToString("s");
        statePayload["last_reset_monthly"] = monthStartsAt.UtcDateTime.ToString("s");
        statePayload["last_reset_daily"] = dayStartsAt.UtcDateTime.ToString("s");

        return statePayload;
    }

    private static MqttDeviceDiscoveryConfig GenerateDiscoveryDevice(ElectricityData data)
    {
        var deviceIdentifier = string.Concat(Manufacturer.ToLower(), "_", data.UserId);
        var stateTopic = $"sgcc_electricity/{deviceIdentifier}/state";

        var device = new MqttDiscoveryDevice
        {
            Name = $"SGCC Electricity {data.UserId}",
            Identifiers = [deviceIdentifier],
            Manufacturer = Manufacturer,
            Model = data.UserId,
            SoftwareVersion = typeof(Program).Assembly.GetName().Version?.ToString(),
        };

        var origin = new MqttDiscoveryConfigOrigin
        {
            Name = Manufacturer,
            SoftwareVersion = typeof(Program).Assembly.GetName().Version?.ToString(),
            SupportUrl = Url,
        };

        var discovery = new MqttDeviceDiscoveryConfig
        {
            Device = device,
            Origin = origin,
            // Shared with all sensors.
            StateTopic = stateTopic,
        };

        AddNewSensorToDiscovery(
            discovery,
            "Balance",
            deviceClass: HomeAssistantDeviceClass.MONETARY,
            unit: CurrencyUnit,
            icon: CurrencyIcon);

        AddNewSensorToDiscovery(
            discovery,
            "Last Update Time",
            deviceClass: HomeAssistantDeviceClass.DATE,
            icon: DateIcon);

        AddNewSensorToDiscovery(
            discovery,
            "Last Day Usage",
            stateClass: MqttDiscoveryStateClass.Total,
            resetType: HomeAssistantResetType.Daily,
            deviceClass: HomeAssistantDeviceClass.ENERGY,
            unit: HomeAssistantUnits.ENERGY_KILO_WATT_HOUR);

        AddNewSensorToDiscovery(
            discovery,
            "Current Year Charge",
            deviceClass: HomeAssistantDeviceClass.MONETARY,
            unit: CurrencyUnit,
            icon: CurrencyIcon);

        AddNewSensorToDiscovery(
            discovery,
            "Current Year Usage",
            stateClass: MqttDiscoveryStateClass.Total,
            resetType: HomeAssistantResetType.Yearly,
            deviceClass: HomeAssistantDeviceClass.ENERGY,
            unit: HomeAssistantUnits.ENERGY_KILO_WATT_HOUR);

        AddNewSensorToDiscovery(
            discovery,
            "Last Month Charge",
            deviceClass: HomeAssistantDeviceClass.MONETARY,
            unit: CurrencyUnit,
            icon: CurrencyIcon);

        AddNewSensorToDiscovery(
            discovery,
            "Last Month Usage",
            resetType: HomeAssistantResetType.Monthly,
            stateClass: MqttDiscoveryStateClass.Total,
            deviceClass: HomeAssistantDeviceClass.ENERGY,
            unit: HomeAssistantUnits.ENERGY_KILO_WATT_HOUR);

        return discovery;
    }

    private static void AddNewSensorToDiscovery(
        MqttDeviceDiscoveryConfig discovery,
        string name,
        MqttDiscoveryStateClass? stateClass = null,
        HomeAssistantResetType? resetType = null,
        HomeAssistantDeviceClass? deviceClass = null,
        HomeAssistantUnits? unit = null,
        string? icon = null)
    {
        var nameSnakeCase = name.Replace(" ", "_").ToLowerInvariant();
        var uniqueId = string.Concat(discovery.Device.Identifiers!.First(), "_", nameSnakeCase);

        var sensor = new MqttSensorDiscoveryConfig
        {
            Name = name,
            UniqueId = uniqueId,
            Icon = icon,
            StateClass = stateClass,
            UnitOfMeasurement = unit?.Value,
            DeviceClass = deviceClass?.Value,
            ValueTemplate = $"{{{{ value_json.{nameSnakeCase} }}}}",
            LastResetValueTemplate = stateClass != MqttDiscoveryStateClass.Total
                ? null
                : resetType switch
                {
                    HomeAssistantResetType.Daily => "{{ value_json.last_reset_daily }}",
                    HomeAssistantResetType.Monthly => "{{ value_json.last_reset_monthly }}",
                    HomeAssistantResetType.Yearly => "{{ value_json.last_reset_yearly }}",
                    _ => null,
                },
        };

        // As the docs suggest, the key of cmps object is not necessarily to be equal to unique_id.
        // See https://www.home-assistant.io/integrations/mqtt/#discovery-messages
        discovery.AddComponent(nameSnakeCase, sensor);
    }

    private enum HomeAssistantResetType
    {
        Daily,
        Monthly,
        Yearly,
    }
}

public record MqttPublishingOptions(
    string Host,
    int Port,
    string? Username = null,
    string? Password = null,
    int MaxAttempts = 5);

public sealed class MqttLoggerForwarder(ILogger logger) : IMqttNetLogger
{
    public void Publish(MqttNetLogLevel logLevel, string source, string message, object[] parameters, Exception exception)
    {
        logger.Log(
            logLevel switch
            {
                MqttNetLogLevel.Error => LogLevel.Error,
                MqttNetLogLevel.Warning => LogLevel.Warning,
                MqttNetLogLevel.Info => LogLevel.Information,
                MqttNetLogLevel.Verbose => LogLevel.Debug,
                _ => LogLevel.Trace,
            },
            exception,
            "[{Source}] {Message}",
            source,
            message);
    }

    public bool IsEnabled => true;
}
