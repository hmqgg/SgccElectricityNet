namespace SgccElectricityNet.Worker.Services.Captcha;

public interface ICaptchaService;

public interface ISliderCaptchaService : ICaptchaService
{
    Task<int> SolveAsync(byte[] image, CancellationToken cancellationToken = default);
}

public interface ISmsCaptchaService : ICaptchaService
{
    Task<string> SolveAsync(CancellationToken cancellationToken = default);
}
