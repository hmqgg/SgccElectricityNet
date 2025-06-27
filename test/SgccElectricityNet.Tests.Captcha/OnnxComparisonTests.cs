using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SgccElectricityNet.Worker.Services.Captcha;
using Xunit;
using Xunit.Abstractions;

namespace SgccElectricityNet.Tests.Captcha;

public sealed class OnnxComparisonTests : IDisposable
{
    private static OnnxCaptchaService MakeCaptchaService()
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

    private readonly ITestOutputHelper _output;
    private readonly OnnxCaptchaService _captchaService;
    private readonly PythonOnnxCaptchaService _pythonService;

    public OnnxComparisonTests(ITestOutputHelper output)
    {
        _output = output;
        _captchaService = MakeCaptchaService();
        _pythonService = new PythonOnnxCaptchaService();

        _output.WriteLine("✓ Test setup completed");
    }

    [Theory]
    [ClassData(typeof(ImageBytesTestData))]
    public async Task E2E_Consistency_Test(byte[] imageBytes)
    {
        // Run C# implementation
        var csharpResult = await _captchaService.SolveAsync(imageBytes);

        // Run Python implementation
        var pythonResult = await _pythonService.SolveAsync(imageBytes);

        _output.WriteLine($"  - C# Result: {csharpResult}");
        _output.WriteLine($"  - Python Result: {pythonResult}");

        // Allow for small differences in coordinate calculation
        const int tolerance = 5; // pixels
        var difference = Math.Abs(csharpResult - pythonResult);

        Assert.True(difference <= tolerance,
            $"× Gap position difference too large. C#: {csharpResult}, Python: {pythonResult}, Difference: {difference}, Tolerance: {tolerance}");

        _output.WriteLine($"✓ E2E test passed with difference: {difference} pixels");
    }


    [Theory]
    [ClassData(typeof(ImageBytesTestData))]
    public async Task Multiple_Images_Consistency_Test(byte[] imageBytes)
    {
        var results = new List<(int csharp, int python)>();

        for (var i = 0; i < 3; i++)
        {
            var csharpResult = await _captchaService.SolveAsync(imageBytes);
            var pythonResult = await _pythonService.SolveAsync(imageBytes);

            results.Add((csharp: csharpResult, python: pythonResult));

            _output.WriteLine($"  - Run {i + 1}: C# = {csharpResult}, Python = {pythonResult}");
        }

        // All C# results should be identical
        var firstCSharpResult = results[0].csharp;
        Assert.All(results, r => Assert.Equal(firstCSharpResult, r.csharp));

        // All Python results should be identical
        var firstPythonResult = results[0].python;
        Assert.All(results, r => Assert.Equal(firstPythonResult, r.python));

        _output.WriteLine("✓ Multiple runs consistency test passed");
    }

    [Fact]
    public async Task Error_Handling_Test()
    {
        // Test with empty byte array
        var argEx = await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await _captchaService.SolveAsync([]));

        _output.WriteLine($"  - Empty image data throws: {argEx.GetType().Name}");

        // Test with invalid image data
        var invalidData = new byte[] { 1, 2, 3, 4, 5 };
        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await _captchaService.SolveAsync(invalidData));

        _output.WriteLine($"  - Invalid image data throws: {ex.GetType().Name}");
        _output.WriteLine("✓ Error handling test passed");
    }

    public void Dispose()
    {
        _captchaService.Dispose();
    }
}
