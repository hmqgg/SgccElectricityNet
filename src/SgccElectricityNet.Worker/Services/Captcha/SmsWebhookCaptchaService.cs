using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace SgccElectricityNet.Worker.Services.Captcha;

public sealed class SmsWebhookCaptchaService(HttpClient client, IOptions<SmsWebhookCaptchaOptions> options, ILogger<SmsWebhookCaptchaService> logger) : ISmsCaptchaService
{
    public async Task<string> SolveAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Getting SMS code from webhook: {WebhookUrl}", options.Value.WebhookUrl);
        var response = await client.GetFromJsonAsync<SmsCodeResponse>(options.Value.WebhookUrl, cancellationToken);
        if (string.IsNullOrEmpty(response?.Code))
        {
            logger.LogError("Failed to retrieve SMS code from webhook: {WebhookUrl}", options.Value.WebhookUrl);
            throw new ApplicationException("Failed to retrieve SMS code from webhook.");
        }

        logger.LogInformation("Received SMS code: {Code}", response.Code);
        return response.Code.Trim();
    }
}

public record SmsCodeResponse(string Code);

public record SmsWebhookCaptchaOptions(string WebhookUrl);
