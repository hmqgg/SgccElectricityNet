using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SgccElectricityNet.Worker.Services.Captcha;

public sealed class RecognizerCaptchaService : ISliderCaptchaService, IDisposable
{
    private readonly ILogger<RecognizerCaptchaService> _logger;
    private readonly InferenceSession _inferenceSession;
    private readonly string _inputName;

    // Model constants.
    private const int InputSize = 416;
    private const float CompensationFactor = 1.03f;

    public RecognizerCaptchaService(IOptions<RecognizerCaptchaOptions> options, ILogger<RecognizerCaptchaService> logger)
    {
        _logger = logger;
        var modelPath = options.Value.ModelPath ?? throw new ArgumentNullException(nameof(options.Value.ModelPath));
        try
        {
            _inferenceSession = new InferenceSession(modelPath);
            _inputName = _inferenceSession.InputMetadata.Keys.First();
            _logger.LogInformation("ONNX model loaded successfully from {ModelPath}", options.Value.ModelPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load ONNX model from {ModelPath}", options.Value.ModelPath);
            throw;
        }
    }

    public Task<int> SolveAsync(byte[] image, CancellationToken cancellationToken = default)
    {
        using var img = Image.Load<Rgb24>(image);
        var originalWidth = img.Width;

        var inputTensor = PreprocessImage(img);
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_inputName, inputTensor),
        };
        using var results = _inferenceSession.Run(inputs);
        var bestBoxX = Postprocess(results);
        _logger.LogInformation("Best box X-coordinate: {BestBoxX}", bestBoxX);
        var distance = (int)Math.Round(bestBoxX / InputSize * originalWidth * CompensationFactor);
        return Task.FromResult(distance);
    }

    private static DenseTensor<float> PreprocessImage(Image<Rgb24> image)
    {
        // Resize the image to the input size
        image.Mutate(x => x.Resize(InputSize, InputSize));

        var inputTensor = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        for (var y = 0; y < InputSize; y++)
        {
            for (var x = 0; x < InputSize; x++)
            {
                var pixel = image[x, y];
                inputTensor[0, 0, y, x] = pixel.R / 255f;
                inputTensor[0, 1, y, x] = pixel.G / 255f;
                inputTensor[0, 2, y, x] = pixel.B / 255f;
            }
        }

        return inputTensor;
    }

    private float Postprocess(IReadOnlyCollection<NamedOnnxValue> results)
    {
        var output = results.FirstOrDefault()?.AsTensor<float>();
        if (output == null)
        {
            _logger.LogWarning("No gaps were detected in the captcha image");
            return 0;
        }

        var detections = new List<DetectionBox>();

        // The output is transposed, with shape [1, 5, 8400].
        // We iterate through the 8400 detections.
        var detectionCount = output.Dimensions[2];
        for (var i = 0; i < detectionCount; i++)
        {
            // The data for each detection is strided.
            var confidence = output[0, 4, i];
            // Add a confidence threshold if needed, e.g., if (confidence < 0.5) continue;

            detections.Add(new DetectionBox(
                X: output[0, 0, i],
                Y: output[0, 1, i],
                Width: output[0, 2, i],
                Height: output[0, 3, i],
                Confidence: confidence
            ));
        }

        if (detections.Count == 0)
        {
            _logger.LogWarning("No gaps passed the confidence threshold");
            return 0;
        }

        var bestDetection = detections.MaxBy(d => d.Confidence);
        var x1 = bestDetection.X - bestDetection.Width / 2;
        _logger.LogInformation("Detected gap with confidence {Confidence} at X-coordinate {X}", bestDetection.Confidence, x1);

        return x1;
    }

    public void Dispose()
    {
        _inferenceSession.Dispose();
    }

    private readonly record struct DetectionBox(float X, float Y, float Width, float Height, float Confidence);
}

public record RecognizerCaptchaOptions(string? ModelPath);
