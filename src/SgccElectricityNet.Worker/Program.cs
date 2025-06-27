using Coravel;
using SgccElectricityNet.Worker.Invocables;
using SgccElectricityNet.Worker.Services.Captcha;
using SgccElectricityNet.Worker.Services.Fetcher;
using SgccElectricityNet.Worker.Services.Publishing;
using Tomlyn.Extensions.Configuration;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddCaptchaService(builder.Configuration.GetSection("Captcha"))
    .AddFetcherService(builder.Configuration.GetSection("Fetcher"))
    .AddPublishingServices(builder.Configuration.GetSection("Publishers"));

builder.Services.AddScheduler();
builder.Services.AddScoped<UpdateInvocable>();

builder.Configuration.Sources.Clear();
builder.Configuration
    .AddTomlFile("appsettings.toml", true, true)
    .AddTomlFile($"appsettings.{builder.Environment.EnvironmentName}.toml", true, true)
    .AddEnvironmentVariables("SGCC_");

var host = builder.Build();

var cron = host.Services.GetService<IConfiguration>()?.GetValue<string>("Schedule");
if (string.IsNullOrEmpty(cron))
{
    // Defaults to 9:00 every day.
    cron = "0 9 * * *";
}
host.Services.UseScheduler(scheduler =>
{
    scheduler
        .Schedule<UpdateInvocable>()
        .Cron(cron)
        .Zoned(
            TimeZoneInfo.CreateCustomTimeZone(
                nameof(SgccElectricityNet),
                TimeSpan.FromHours(+8),
                nameof(SgccElectricityNet),
                nameof(SgccElectricityNet)))
        .RunOnceAtStart();
});

host.Run();
