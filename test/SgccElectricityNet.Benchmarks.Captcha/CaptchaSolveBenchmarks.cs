using System.Reflection;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgccElectricityNet.Worker.Services.Captcha;

namespace SgccElectricityNet.Benchmarks.Captcha;

[MemoryDiagnoser]
public class CaptchaSolveBenchmarks
{
    private byte[]? _imageBytes;
    private OnnxCaptchaService? _onnxService;
    private RecognizerCaptchaService? _recognizerService;

    [GlobalSetup]
    public void Setup()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var testDirectory = Path.GetDirectoryName(assemblyLocation)!;

        var loggerFactory = NullLoggerFactory.Instance;
        // Setup OnnxCaptchaService
        var onnxModelPath = Path.Combine(testDirectory, "assets", "captcha.onnx");
        if (!File.Exists(onnxModelPath))
        {
            throw new FileNotFoundException($"ONNX model not found at: {onnxModelPath}");
        }
        IOptions<OnnxCaptchaOptions> onnxOptions = Options.Create(new OnnxCaptchaOptions(onnxModelPath));
        ILogger<OnnxCaptchaService> onnxLogger = loggerFactory.CreateLogger<OnnxCaptchaService>();
        _onnxService = new OnnxCaptchaService(onnxOptions, onnxLogger);

        // Setup RecognizerCaptchaService
        var recognizerModelPath = Path.Combine(testDirectory, "assets", "recognizer_single_cls.onnx");
        if (!File.Exists(recognizerModelPath))
        {
            throw new FileNotFoundException($"Recognizer model not found at: {recognizerModelPath}");
        }
        IOptions<RecognizerCaptchaOptions> recognizerOptions = Options.Create(new RecognizerCaptchaOptions(recognizerModelPath));
        ILogger<RecognizerCaptchaService> recognizerLogger = loggerFactory.CreateLogger<RecognizerCaptchaService>();
        _recognizerService = new RecognizerCaptchaService(recognizerOptions, recognizerLogger);

        // Load image data
        var testImagePath = Path.Combine(testDirectory, "assets", "test_images", "test_1.png");
        if (!File.Exists(testImagePath))
        {
            throw new FileNotFoundException($"Image file not found at: {testImagePath}");
        }
        _imageBytes = File.ReadAllBytes(testImagePath);
    }

    [Benchmark(Baseline = true)]
    public async Task<int> Onnx() => await _onnxService!.SolveAsync(_imageBytes!);

    [Benchmark]
    public async Task<int> Recognizer() => await _recognizerService!.SolveAsync(_imageBytes!);

    [GlobalCleanup]
    public void Cleanup()
    {
        _onnxService?.Dispose();
        _recognizerService?.Dispose();
    }
}
