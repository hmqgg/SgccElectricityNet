using Microsoft.Extensions.Options;

namespace SgccElectricityNet.Worker.Services.Captcha;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCaptchaService(
        this IServiceCollection services,
        IConfigurationSection captchaSection)
    {
        services.AddOptions();
        services.AddLogging();

        var type = captchaSection.GetValue<CaptchaServiceType>("Type");
        switch (type)
        {
            case CaptchaServiceType.Onnx:
                services.AddSingleton(Options.Create(captchaSection.Get<OnnxCaptchaOptions>()!));
                services.AddScoped<ICaptchaService, OnnxCaptchaService>();
                break;
            case CaptchaServiceType.Recognizer:
                services.AddSingleton(Options.Create(captchaSection.Get<RecognizerCaptchaOptions>()!));
                services.AddScoped<ICaptchaService, RecognizerCaptchaService>();
                break;
            case CaptchaServiceType.SmsWebhook:
                services.AddHttpClient();
                services.AddSingleton(Options.Create(captchaSection.Get<SmsWebhookCaptchaOptions>()!));
                services.AddTransient<ICaptchaService, SmsWebhookCaptchaService>();
                break;
            default:
                throw new NotImplementedException("Unknown captcha service type: " + type);
        }

        return services;
    }
}

public enum CaptchaServiceType
{
    Onnx,
    Recognizer,
    SmsWebhook,
}
