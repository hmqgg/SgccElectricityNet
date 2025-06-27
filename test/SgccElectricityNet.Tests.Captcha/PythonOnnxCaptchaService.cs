using System.Diagnostics;
using System.Text.Json;
using SgccElectricityNet.Worker.Services.Captcha;

namespace SgccElectricityNet.Tests.Captcha;

public class PythonOnnxCaptchaService : ICaptchaService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<int> SolveAsync(byte[] image, CancellationToken cancellationToken = default)
    {
        PythonOnnxResult result = await RunPythonTestAsync(image, cancellationToken);
        // Compensate as the python data_fetcher indicates.
        return (int)Math.Round(result.Distance * 1.03);
    }

    private static async Task<PythonOnnxResult> RunPythonTestAsync(byte[] image, CancellationToken cancellationToken = default)
    {
        var workingDirectory = Path.GetDirectoryName(typeof(PythonOnnxCaptchaService).Assembly.Location);
        var pythonScriptPath = Path.Combine(workingDirectory!, "python_scripts", "test_onnx.py");
        var modelPath = Path.Combine(workingDirectory!, "assets", "captcha.onnx");
        var tempImagePath = Path.GetTempFileName();
        await File.WriteAllBytesAsync(tempImagePath, image, cancellationToken);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"\"{pythonScriptPath}\" --image \"{tempImagePath}\" --model \"{modelPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };

        using var process = new Process();
        process.StartInfo = processStartInfo;
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Python script failed with exit code {process.ExitCode}. Stderr: {error} Stdout: {output}");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("Python script returned empty output");
        }

        try
        {
            return JsonSerializer.Deserialize<PythonOnnxResult>(output, JsonOptions) ?? throw new InvalidOperationException("Failed to deserialize Python output");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse Python output as JSON. Output: {output}", ex);
        }
    }
}

public record PythonOnnxResult(int Distance, string Error);
