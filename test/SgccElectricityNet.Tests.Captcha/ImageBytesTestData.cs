using System.Reflection;
using Xunit;

namespace SgccElectricityNet.Tests.Captcha;

public class ImageBytesTestData : TheoryData<byte[]>
{
    public ImageBytesTestData()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var testDirectory = Path.GetDirectoryName(assemblyLocation)!;
        var testImagePath = Path.Combine(testDirectory, "assets", "test_images");
        foreach (var file in Directory.EnumerateFiles(testImagePath, "*.png"))
        {
            Add(File.ReadAllBytes(file));
        }

        if (Count == 0)
        {
            throw new FileNotFoundException($"Image files not found at: {testImagePath}");
        }
    }
}
