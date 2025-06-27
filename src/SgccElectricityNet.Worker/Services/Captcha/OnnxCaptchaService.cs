using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SgccElectricityNet.Worker.Services.Captcha;

public sealed class OnnxCaptchaService : ISliderCaptchaService, IDisposable
{
    private readonly ILogger _logger;
    private readonly InferenceSession _inferenceSession;
    private readonly string _inputName;

    // Model constants.
    private const int InputSize = 416;
    private const float ConfidenceThreshold = 0.7f;
    private const float NmsThreshold = 0.6f;
    private const float CompensationFactor = 1.03f;

    public OnnxCaptchaService(IOptions<OnnxCaptchaOptions> options, ILogger<OnnxCaptchaService> logger)
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

    public async Task<int> SolveAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        try
        {
            using var image = Image.Load<Rgb24>(imageBytes);

            // Preprocess image
            var preprocessedData = PreprocessImage(image);
            cancellationToken.ThrowIfCancellationRequested();

            // Run inference
            var outputs = await RunInferenceAsync(preprocessedData);
            cancellationToken.ThrowIfCancellationRequested();

            // Post-process results
            var detectionBoxes = PostprocessResults(outputs);
            cancellationToken.ThrowIfCancellationRequested();

            if (detectionBoxes.Length == 0)
            {
                _logger.LogWarning("No gaps were detected in the captcha image");
                return 0;
            }

            // Return the x-coordinate of the first detected gap
            var gapPosition = detectionBoxes[0].X1 * CompensationFactor;
            _logger.LogInformation("Detected gap at position: {Position}", gapPosition);

            return (int)Math.Round(gapPosition);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing captcha image");
            throw;
        }
    }

    private static Tensor<float> PreprocessImage(Image<Rgb24> image)
    {
        // Resize image to model input size
        using var resizedImage = image.Clone(ctx => ctx.Resize(InputSize, InputSize));

        // Convert to tensor format: [1, 3, 416, 416]
        var dimensions = new[] { 1, 3, InputSize, InputSize };
        var tensor = new DenseTensor<float>(dimensions);

        resizedImage.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < InputSize; y++)
            {
                var pixelRow = accessor.GetRowSpan(y);
                for (var x = 0; x < InputSize; x++)
                {
                    var pixel = pixelRow[x];

                    // Normalize to [0, 1] and convert to CHW format
                    tensor[0, 0, y, x] = pixel.R / 255.0f; // Red channel
                    tensor[0, 1, y, x] = pixel.G / 255.0f; // Green channel
                    tensor[0, 2, y, x] = pixel.B / 255.0f; // Blue channel
                }
            }
        });

        return tensor;
    }

    private ValueTask<Tensor<float>> RunInferenceAsync(Tensor<float> inputTensor)
    {
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_inputName, inputTensor),
        };

        using var results = _inferenceSession.Run(inputs);
        var output = results[0].AsTensor<float>();

        // Could it optimize performance with Task.Run?
        return ValueTask.FromResult(output);
    }

    private static DetectionBox[] PostprocessResults(Tensor<float> prediction)
    {
        // Get prediction dimensions
        var shape = prediction.Dimensions.ToArray();
        var numDetections = shape[1];
        var numClasses = shape[2] - 5;

        var boxes = new List<(DetectionBox Box, int ClassId)>();

        // Filter boxes by confidence threshold and find the best class
        for (var i = 0; i < numDetections; i++)
        {
            var confidence = prediction[0, i, 4];
            if (confidence <= ConfidenceThreshold) continue;

            var classScores = new float[numClasses];
            for (var j = 0; j < numClasses; j++)
            {
                classScores[j] = prediction[0, i, 5 + j];
            }

            var maxClassScore = 0.0f;
            var classId = -1;
            for (var j = 0; j < numClasses; j++)
            {
                if (classScores[j] > maxClassScore)
                {
                    maxClassScore = classScores[j];
                    classId = j;
                }
            }

            if (classId == -1) continue;

            var x = prediction[0, i, 0];
            var y = prediction[0, i, 1];
            var w = prediction[0, i, 2];
            var h = prediction[0, i, 3];

            boxes.Add((new DetectionBox
            {
                X1 = x - w / 2,
                Y1 = y - h / 2,
                X2 = x + w / 2,
                Y2 = y + h / 2,
                Confidence = confidence,
            }, classId));
        }

        if (boxes.Count == 0)
            return [];

        // Group boxes by class and apply NMS for each class
        var finalBoxes = new List<DetectionBox>();
        var groupedBoxes = boxes.GroupBy(b => b.ClassId);

        foreach (var group in groupedBoxes)
        {
            var classBoxes = group.Select(b => b.Box).ToArray();
            var nmsResults = ApplyNonMaximumSuppression(classBoxes, NmsThreshold);
            finalBoxes.AddRange(nmsResults);
        }

        // Sort by confidence before returning
        return finalBoxes.OrderByDescending(b => b.Confidence).ToArray();
    }

    private static DetectionBox[] ApplyNonMaximumSuppression(DetectionBox[] boxes, float threshold)
    {
        if (boxes.Length == 0)
            return [];

        // Sort by confidence in descending order
        var sortedBoxes = boxes.OrderByDescending(b => b.Confidence).ToArray();
        var keep = new List<DetectionBox>();
        var suppressed = new bool[sortedBoxes.Length];

        for (var i = 0; i < sortedBoxes.Length; i++)
        {
            if (suppressed[i])
                continue;

            keep.Add(sortedBoxes[i]);

            // Suppress overlapping boxes
            for (var j = i + 1; j < sortedBoxes.Length; j++)
            {
                if (suppressed[j])
                    continue;

                var iou = CalculateIoU(sortedBoxes[i], sortedBoxes[j]);
                if (iou > threshold)
                {
                    suppressed[j] = true;
                }
            }
        }

        return keep.ToArray();
    }

    private static float CalculateIoU(DetectionBox box1, DetectionBox box2)
    {
        // Calculate intersection area
        var x1 = Math.Max(box1.X1, box2.X1);
        var y1 = Math.Max(box1.Y1, box2.Y1);
        var x2 = Math.Min(box1.X2, box2.X2);
        var y2 = Math.Min(box1.Y2, box2.Y2);

        var intersectionArea = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);

        // Calculate union area
        var box1Area = (box1.X2 - box1.X1) * (box1.Y2 - box1.Y1);
        var box2Area = (box2.X2 - box2.X1) * (box2.Y2 - box2.Y1);
        var unionArea = box1Area + box2Area - intersectionArea;

        return unionArea > 0 ? intersectionArea / unionArea : 0;
    }

    public void Dispose()
    {
        _inferenceSession.Dispose();
    }

    private readonly record struct DetectionBox(float X1, float Y1, float X2, float Y2, float Confidence);
}

public record OnnxCaptchaOptions(string? ModelPath);
