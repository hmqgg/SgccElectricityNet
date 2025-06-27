using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using SgccElectricityNet.Worker.Services.Captcha;
using SgccElectricityNet.Worker.Services.Fetcher;
using Xunit;
using Xunit.Abstractions;

namespace SgccElectricityNet.Tests.Fetcher;

public sealed class PlaywrightIntegrationTests(ITestOutputHelper output) : IAsyncLifetime
{
    private HttpClient _client = null!;
    private PlaywrightFetcherService _fetcherService = null!;
    private PlaywrightBrowserFactory _playwrightBrowserFactory = null!;

    public Task InitializeAsync()
    {
        var env = new HostingEnvironment
        {
            EnvironmentName = Environments.Development,
        };
        _playwrightBrowserFactory = new PlaywrightBrowserFactory(env);
        _client = new HttpClient();
        _fetcherService = MakeFetcherService();
        output.WriteLine("✓ Test setup completed");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _playwrightBrowserFactory.DisposeAsync();
        _client.Dispose();
        output.WriteLine("✓ Test cleanup completed");
    }

    private SmsWebhookCaptchaService MakeCaptchaService()
    {
        var options = Options.Create(
            new SmsWebhookCaptchaOptions(
                Environment.GetEnvironmentVariable("SGCC__SMS_WEBHOOK_URL")
                ?? throw new InvalidOperationException("SMS Webhook URL not set")));

        var logger = output.BuildLoggerFor<SmsWebhookCaptchaService>();
        return new SmsWebhookCaptchaService(_client, options, logger);
    }

    private PlaywrightFetcherService MakeFetcherService()
    {
        var options = Options.Create(new FetcherOptions(Environment.GetEnvironmentVariable("SGCC__USERNAME"), null));
        var logger = output.BuildLoggerFor<PlaywrightFetcherService>();
        return new PlaywrightFetcherService(options, _playwrightBrowserFactory, MakeCaptchaService(), logger);
    }

    [Fact]
    public void Create_Instance_Test()
    {
        Assert.NotNull(_fetcherService);

        output.WriteLine($"✓ Fetcher service created successfully: {_fetcherService.GetType().Name}");
    }

    [Fact(Skip = "Skipped due to potential network issues")]
    public async Task E2E_Fetch_Test()
    {
        var userId = Environment.GetEnvironmentVariable("SGCC__USERID");
        Assert.NotNull(userId);
        using var cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        output.WriteLine($"Starting E2E fetch test for user: {userId}");

        var data = await _fetcherService.FetchAsync(userId, cancellationToken.Token);

        Assert.NotNull(data);
        output.WriteLine($"  - Result: {data}");
        Assert.NotEmpty(data.RecentDailyUsages);
        output.WriteLine($"  - Recent Daily Usages Count: {data.RecentDailyUsages.Count}");
        Assert.NotEmpty(data.MonthlySummaries);
        output.WriteLine($"  - Monthly Summaries Count: {data.MonthlySummaries.Count}");
        Assert.Equal(userId, data.UserId);
        output.WriteLine($"  - Same User ID: {data.UserId}");

        output.WriteLine($"✓ E2E fetch test completed successfully for user: {userId}");
    }
}
