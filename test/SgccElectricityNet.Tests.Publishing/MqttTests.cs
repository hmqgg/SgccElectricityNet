using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Bogus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Exceptions;
using MQTTnet.Server;
using SgccElectricityNet.Worker.Models;
using SgccElectricityNet.Worker.Services.Publishing;
using Xunit.Abstractions;

namespace SgccElectricityNet.Tests.Publishing;

public class MqttTests(ITestOutputHelper output)
{
    private readonly Faker<ElectricityData> _dataFaker = new Faker<ElectricityData>()
        .CustomInstantiator(f => new ElectricityData(
            UserId: f.Random.String2(10, "1234567890"),
            Balance: f.Random.Decimal(-1000, 1000),
            LastUpdateTime: f.Date.PastDateOnly(),
            LastDayUsage: f.Random.Double(0, 50),
            CurrentYearUsage: f.Random.Double(0, 10000),
            CurrentYearCharge: f.Random.Decimal(0, 5000),
            LastMonthUsage: f.Random.Double(0, 100),
            LastMonthCharge: f.Random.Decimal(0, 50),
            RecentDailyUsages: [],
            MonthlySummaries: []));

    private static MqttServer MakeMqttServer(out int port, ConcurrentBag<MqttApplicationMessage> messages)
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();

        var mqttServerFactory = new MqttServerFactory();
        var mqttServerOptions = mqttServerFactory
            .CreateServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(port)
            .Build();
        var server = mqttServerFactory.CreateMqttServer(mqttServerOptions);
        server.InterceptingPublishAsync  += args =>
        {
            messages.Add(args.ApplicationMessage);
            return Task.CompletedTask;
        };

        return server;
    }

    [Fact]
    public async Task Publish_RandomData_Test()
    {
        // Arrange.
        var messages = new ConcurrentBag<MqttApplicationMessage>();
        using var server = MakeMqttServer(out var port, messages);
        await server.StartAsync();

        var options = Options.Create(new MqttPublishingOptions("localhost", port, MaxAttempts: 3));
        var logger = output.BuildLoggerFor<MqttPublishingService>();
        var service = new MqttPublishingService(options, logger);

        var data = _dataFaker.Generate();

        // Act.
        await service.PublishAsync(data);

        // Assert.
        Assert.StartsWith(
            $"Published state for user {data.UserId}",
            logger.Entries.Last(e => e.LogLevel == LogLevel.Information).Message);
        output.WriteLine($"  ✓ Logged: Published state for user {data.UserId}...");

        Assert.Equal(2, messages.Count);
        output.WriteLine("  ✓ Received: 2 messages.");

        Assert.Contains(messages, msg => msg.Topic.StartsWith("homeassistant/device"));
        output.WriteLine("  ✓ Contains: 1 discovery topic message.");

        Assert.Contains(messages, msg => msg.Topic.StartsWith("sgcc_electricity/"));
        output.WriteLine("  ✓ Contains: 1 state topic message.");

        output.WriteLine("✓ Unit test passed.");
    }

    [Fact]
    public async Task PublishBatch_RandomData_Test()
    {
        // Arrange.
        var messages = new ConcurrentBag<MqttApplicationMessage>();
        using var server = MakeMqttServer(out var port, messages);
        await server.StartAsync();

        var options = Options.Create(new MqttPublishingOptions("localhost", port, MaxAttempts: 3));
        var logger = output.BuildLoggerFor<MqttPublishingService>();
        var service = new MqttPublishingService(options, logger);

        var lotsOfData = _dataFaker.GenerateBetween(1, 10);

        // Act.
        await service.PublishBatchAsync(lotsOfData);
        output.WriteLine($"✓ Test setup completed with data count: {lotsOfData.Count}");

        // Assert.
        Assert.Equal(2 * lotsOfData.Count, messages.Count);
        output.WriteLine($"  ✓ Received: {messages.Count} messages.");

        var discoveryCount = messages.Count(msg => msg.Topic.StartsWith("homeassistant/device"));
        Assert.Equal(lotsOfData.Count, discoveryCount);
        output.WriteLine($"  ✓ Contains: {discoveryCount} discovery topic messages.");

        var stateCount = messages.Count(msg => msg.Topic.StartsWith("sgcc_electricity/"));
        Assert.Equal(lotsOfData.Count, stateCount);
        output.WriteLine($"  ✓ Contains: {stateCount} state topic messages.");

        output.WriteLine("✓ Unit test passed.");
    }

    [Fact]
    public async Task Publish_Retry_Test()
    {
        // Arrange.
        var messages = new ConcurrentBag<MqttApplicationMessage>();
        using var server = MakeMqttServer(out var port, messages);
        await server.StartAsync();
        server.AcceptNewConnections = false;

        const int maxAttempts = 3;
        var options = Options.Create(new MqttPublishingOptions("localhost", port, MaxAttempts: maxAttempts));
        var logger = output.BuildLoggerFor<MqttPublishingService>();
        var service = new MqttPublishingService(options, logger);

        var data = _dataFaker.Generate();

        // Act & Assert.
        var ex = await Assert.ThrowsAnyAsync<MqttCommunicationException>(() => service.PublishAsync(data));
        output.WriteLine($"  ✓ Throws: {ex.Message}");

        Assert.Equal(maxAttempts, logger.Entries.Count(e => e.LogLevel == LogLevel.Error));
        output.WriteLine($"  ✓ Logged: {maxAttempts} error messages for retries.");

        output.WriteLine("✓ Unit test passed.");
    }
}
