using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SgccElectricityNet.Worker.Services.Captcha;
using Xunit;
using Xunit.Abstractions;

namespace SgccElectricityNet.Tests.Captcha;

public sealed class RecognizerComparisonTests : IDisposable
{
    private static OnnxCaptchaService MakeOnnxCaptchaService()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var testDirectory = Path.GetDirectoryName(assemblyLocation)!;
        var modelPath = Path.Combine(testDirectory, "assets", "captcha.onnx");

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"ONNX model not found at: {modelPath}");

        var options = Options.Create(new OnnxCaptchaOptions(modelPath));
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OnnxCaptchaService>();
        return new OnnxCaptchaService(options, logger);
    }

    private static RecognizerCaptchaService MakeRecognizerCaptchaService()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var testDirectory = Path.GetDirectoryName(assemblyLocation)!;
        var modelPath = Path.Combine(testDirectory, "assets", "recognizer_single_cls.onnx");

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Recognizer model not found at: {modelPath}");

        var options = Options.Create(new RecognizerCaptchaOptions(modelPath));
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<RecognizerCaptchaService>();
        return new RecognizerCaptchaService(options, logger);
    }

    private readonly ITestOutputHelper _output;
    private readonly OnnxCaptchaService _onnxService;
    private readonly RecognizerCaptchaService _recognizerService;

    public RecognizerComparisonTests(ITestOutputHelper output)
    {
        _output = output;
        _onnxService = MakeOnnxCaptchaService();
        _recognizerService = MakeRecognizerCaptchaService();

        _output.WriteLine("✓ Test setup completed");
    }

    [Theory]
    [ClassData(typeof(ImageBytesTestData))]
    public async Task E2E_Consistency_Test(byte[] imageBytes)
    {
        // Run OnnxCaptchaService implementation
        var onnxResult = await _onnxService.SolveAsync(imageBytes);

        // Run RecognizerCaptchaService implementation
        var recognizerResult = await _recognizerService.SolveAsync(imageBytes);

        _output.WriteLine($"  - Onnx Result: {onnxResult}");
        _output.WriteLine($"  - Recognizer Result: {recognizerResult}");

        // Allow for small differences in coordinate calculation
        var tolerance = 5; // pixels
        var difference = Math.Abs(onnxResult - recognizerResult);

        Assert.True(difference <= tolerance,
            $"Gap position difference too large. Onnx: {onnxResult}, Recognizer: {recognizerResult}, Difference: {difference}, Tolerance: {tolerance}");

        _output.WriteLine($"✓ E2E test passed with difference: {difference} pixels");
    }

    [Theory]
    [ClassData(typeof(ImageBytesTestData))]
    public async Task Multiple_Images_Consistency_Test(byte[] imageBytes)
    {
        var results = new List<int>();

        for (var i = 0; i < 3; i++)
        {
            var recogResult = await _recognizerService.SolveAsync(imageBytes);

            results.Add(recogResult);

            _output.WriteLine($"  - Run {i + 1}: Recognizer = {recogResult}");
        }

        Assert.All(results, r => Assert.Equal(results[0], r));
        _output.WriteLine("✓ Multiple runs consistency test passed");
    }

    [Fact]
    public async Task Error_Handling_Test()
    {
        // Test with empty byte array
        var argEx = await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await _recognizerService.SolveAsync([]));

        _output.WriteLine($"  - Empty image data throws: {argEx.GetType().Name}");

        // Test with invalid image data
        var invalidData = new byte[] { 1, 2, 3, 4, 5 };
        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await _recognizerService.SolveAsync(invalidData));

        _output.WriteLine($"  - Invalid image data throws: {ex.GetType().Name}");

        _output.WriteLine("✓ Error handling test passed");
    }

    public void Dispose()
    {
        _onnxService.Dispose();
        _recognizerService.Dispose();
    }
}
