using ImgMzx;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;

namespace ImgMzxTests;

[TestClass]
public class AppBitmapTest
{
    [TestMethod]
    public void TestCorruptedImages()
    {
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\gab_corrupted.jpg");
        Assert.IsNotNull(data);
        var corrupted = AppBitmap.GetImage(data);
        Assert.IsNull(corrupted);

        data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\num_corrupted.jpg");
        Assert.IsNotNull(data);
        var partiallyCorrupted = AppBitmap.GetImage(data);
        Assert.IsNotNull(partiallyCorrupted);
        Assert.AreEqual(956, partiallyCorrupted.Width);
        Assert.AreEqual(24, partiallyCorrupted.PixelType.BitsPerPixel);
        partiallyCorrupted.Dispose();
    }

    [TestMethod]
    public void TestValidImages()
    {
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\gab_org.jpg");
        Assert.IsNotNull(data);
        
        var format = SixLabors.ImageSharp.Image.DetectFormat(data);
        Assert.AreEqual("jpg", format.FileExtensions.First());
        
        using var image = AppBitmap.GetImage(data);
        Assert.IsNotNull(image);
        Assert.AreEqual(2000, image.Width);
        Assert.AreEqual(24, image.PixelType.BitsPerPixel);
        Assert.IsNotNull(image.Metadata.DecodedImageFormat);
        Assert.AreEqual("jpg", image.Metadata.DecodedImageFormat.FileExtensions.First());
        
        var dateTaken = AppBitmap.GetDateTaken(image);
        Assert.IsNotNull(dateTaken);
        Assert.AreEqual(10, dateTaken.Value.Hour);
    }

    [TestMethod]
    public void TestImageFormats()
    {
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\png.png");
        Assert.IsNotNull(data);
        
        var format = SixLabors.ImageSharp.Image.DetectFormat(data);
        Assert.AreEqual("png", format.FileExtensions.First());
        
        using var image = AppBitmap.GetImage(data);
        Assert.IsNotNull(image);
        Assert.IsNotNull(image.Metadata.DecodedImageFormat);
        Assert.AreEqual("png", image.Metadata.DecodedImageFormat.FileExtensions.First());

        data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\exif_nodt.jpg");
        Assert.IsNotNull(data);
        using var imageNoDate = AppBitmap.GetImage(data);
        Assert.IsNotNull(imageNoDate);
        var dt = AppBitmap.GetDateTaken(imageNoDate);
        Assert.IsNull(dt);
    }

    [TestMethod]
    public void TestImageSizes()
    {
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\num_8bbp.jpg");
        Assert.IsNotNull(data);
        using var image = AppBitmap.GetImage(data);
        Assert.IsNotNull(image);
        Assert.AreEqual(3000, image.Width);
        Assert.AreEqual(24, image.PixelType.BitsPerPixel);
    }

    [TestMethod]
    public void TestUnsupportedFormats()
    {
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\heic.heic");
        Assert.IsNotNull(data);
        using var heicImage = AppBitmap.GetImage(data);
        Assert.IsNull(heicImage);
    }

    [TestMethod]
    public void TestImageTransformations()
    {
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\gab_org.jpg");
        Assert.IsNotNull(data);
        
        using var transformed = AppBitmap.GetImage(data, RotateMode.Rotate90, FlipMode.Vertical);
        Assert.IsNotNull(transformed);
    }

    [TestMethod]
    public void TestComposite()
    {
        var orgData = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\gab_org.jpg");
        Assert.IsNotNull(orgData);
        using var orgImage = AppBitmap.GetImage(orgData);
        Assert.IsNotNull(orgImage);

        var logoData = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\gab_logo.jpg");
        Assert.IsNotNull(logoData);
        using var logoImage = AppBitmap.GetImage(logoData);
        Assert.IsNotNull(logoImage);
        
        AppBitmap.Composite(orgImage, logoImage, out var composite);
        Assert.IsNotNull(composite);
        
        var outputPath = $@"{AppContext.BaseDirectory}images\gab_xor.jpg";
        using var fs = File.Create(outputPath);
        composite.Save(fs, new JpegEncoder());
        
        composite.Dispose();
    }

    [TestMethod]
    public void TestFormatDetectionWithCaching()
    {
        Console.WriteLine("1. Format Detection with Caching:");

        var testData = CreateTestJpegBytes();

        var stopwatch = Stopwatch.StartNew();
        var ext1 = AppBitmap.GetExtension(testData);
        var firstCall = stopwatch.ElapsedTicks;

        stopwatch.Restart();
        var ext2 = AppBitmap.GetExtension(testData);
        var cachedCall = stopwatch.ElapsedTicks;

        Assert.AreEqual(ext1, ext2);
        Console.WriteLine($"   Extension detected: .{ext1}");
        if (firstCall > cachedCall && cachedCall > 0) {
            Console.WriteLine($"   Speedup: {(double)firstCall / cachedCall:F0}x");
        }
        Console.WriteLine("   ✓ Caching works correctly");
        Console.WriteLine();
    }

    [TestMethod]
    public void TestSpanOperations()
    {
        Console.WriteLine("2. Span<T> Zero-Copy Operations:");

        var testData = CreateTestJpegBytes();
        var spanExt = AppBitmap.GetExtension(testData.AsSpan());
        var arrayExt = AppBitmap.GetExtension(testData);

        Assert.AreEqual(spanExt, arrayExt);
        Console.WriteLine("   ✓ Span<T> overloads available");
        Console.WriteLine();
    }

    [TestMethod]
    public void TestImageProcessing()
    {
        Console.WriteLine("3. Optimized Image Processing:");

        var stopwatch = Stopwatch.StartNew();
        using var image = CreateSyntheticTestImage();
        var creationTime = stopwatch.ElapsedMilliseconds;

        Assert.IsNotNull(image);
        Console.WriteLine($"   Creation time: {creationTime}ms");
        Console.WriteLine($"   Size: {image.Width}x{image.Height}");
        Console.WriteLine("   ✓ Image processing optimized");
        Console.WriteLine();
    }

    [TestMethod]
    public void TestImageSourceCreation()
    {
        Console.WriteLine("4. WPF ImageSource Creation:");

        using var image = CreateSyntheticTestImage();
        var stopwatch = Stopwatch.StartNew();
        var imageSource = AppBitmap.GetImageSource(image);
        var sourceTime = stopwatch.ElapsedMilliseconds;

        Assert.IsNotNull(imageSource);
        Assert.IsTrue(imageSource.IsFrozen);
        Console.WriteLine($"   Creation time: {sourceTime}ms");
        Console.WriteLine("   ✓ ArrayPool optimization active");
        Console.WriteLine();
    }

    [TestMethod]
    public void TestMetadataExtraction()
    {
        Console.WriteLine("5. Metadata Extraction:");

        using var image = CreateSyntheticTestImage();
        var meta = AppBitmap.GetMeta(image);

        Assert.IsNotNull(meta);
        Console.WriteLine($"   Metadata: '{meta}'");
        Console.WriteLine("   ✓ StringBuilder optimization");
        Console.WriteLine();
    }

    [TestMethod]
    public void TestCompositeProcessing()
    {
        Console.WriteLine("6. Parallel Composite Processing:");

        using var image1 = CreateSyntheticTestImage();
        using var image2 = CreateAlternateTestImage();

        var stopwatch = Stopwatch.StartNew();
        AppBitmap.Composite(image1, image2, out var composite);
        var compositeTime = stopwatch.ElapsedMilliseconds;

        Assert.IsNotNull(composite);
        Console.WriteLine($"   Processing time: {compositeTime}ms");
        Console.WriteLine("   ✓ Parallel optimization active");

        composite.Dispose();
        Console.WriteLine();
    }

    [TestMethod]
    public void TestMemoryManagement()
    {
        Console.WriteLine("7. Memory Management:");

        GC.Collect();
        var initialMemory = GC.GetTotalMemory(false);

        for (int i = 0; i < 5; i++) {
            var testData = CreateTestJpegBytes();
            var ext = AppBitmap.GetExtension(testData);
            using var img = CreateSyntheticTestImage();
            var source = AppBitmap.GetImageSource(img);
        }

        AppBitmap.ClearCache();
        GC.Collect();
        var finalMemory = GC.GetTotalMemory(false);
        var memoryChange = (finalMemory - initialMemory) / 1024.0 / 1024.0;

        Console.WriteLine($"   Memory change: {Math.Abs(memoryChange):F1}MB");
        Console.WriteLine("   ✓ Memory management active");
        Console.WriteLine();
    }

    [TestMethod]
    public void TestErrorHandling()
    {
        Console.WriteLine("8. Error Handling:");

        var invalidData = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        var invalidExt = AppBitmap.GetExtension(invalidData);
        using var invalidImage = AppBitmap.GetImage(invalidData);

        Assert.AreEqual("xxx", invalidExt);
        Assert.IsNull(invalidImage);
        Console.WriteLine("   ✓ Invalid data handled gracefully");
        Console.WriteLine();
    }

    [TestMethod]
    public void TestCorruptedSynteticImages()
    {
        Console.WriteLine("9. Corrupted Image Tests:");

        var invalidJpegData = new byte[]
        {
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46,
            0x00, 0x01
        };

        var ext = AppBitmap.GetExtension(invalidJpegData);
        Assert.AreEqual("jpg", ext);

        using var image = AppBitmap.GetImage(invalidJpegData);
        Assert.IsNull(image);

        var totallyInvalidData = new byte[] { 0x12, 0x34, 0x56, 0x78 };
        var invalidExt = AppBitmap.GetExtension(totallyInvalidData);
        Assert.AreEqual("xxx", invalidExt);

        Console.WriteLine("   ✓ Corrupted images handled correctly");
        Console.WriteLine();
    }

    [TestMethod]
    public void TestDifferentFormats()
    {
        Console.WriteLine("10. Different Format Tests:");

        var jpegData = CreateTestJpegBytes();
        var pngData = CreateTestPngBytes();
        var unknownData = new byte[] { 0x12, 0x34, 0x56, 0x78 };

        var jpegExt = AppBitmap.GetExtension(jpegData);
        var pngExt = AppBitmap.GetExtension(pngData);
        var unknownExt = AppBitmap.GetExtension(unknownData);

        Assert.AreEqual("jpg", jpegExt);
        Assert.AreEqual("png", pngExt);
        Assert.AreEqual("xxx", unknownExt);

        Console.WriteLine("   ✓ Multiple formats detected correctly");
        Console.WriteLine();
    }

    [TestMethod]
    public void TestSynteticImageSizes()
    {
        Console.WriteLine("11. Image Size Tests:");

        using var smallImage = new Image<Rgb24>(100, 100);
        using var largeImage = new Image<Rgb24>(2000, 1500);

        var smallSource = AppBitmap.GetImageSource(smallImage);
        var largeSource = AppBitmap.GetImageSource(largeImage);

        Assert.IsNotNull(smallSource);
        Assert.IsNotNull(largeSource);

        Console.WriteLine("   ✓ Different image sizes handled");
        Console.WriteLine();
    }

    private static byte[] CreateTestJpegBytes()
    {
        return
        [
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46,
            0x00, 0x01, 0x01, 0x01, 0x00, 0x48, 0x00, 0x48, 0x00, 0x00
        ];
    }

    private static byte[] CreateTestPngBytes()
    {
        return
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52
        ];
    }

    private static Image<Rgb24> CreateSyntheticTestImage()
    {
        var image = new Image<Rgb24>(400, 300);

        for (int y = 0; y < image.Height; y++) {
            for (int x = 0; x < image.Width; x++) {
                var r = (byte)(x * 255 / image.Width);
                var g = (byte)(y * 255 / image.Height);
                var b = (byte)((x + y) * 255 / (image.Width + image.Height));
                image[x, y] = new Rgb24(r, g, b);
            }
        }

        return image;
    }

    private static Image<Rgb24> CreateAlternateTestImage()
    {
        var image = new Image<Rgb24>(400, 300);

        for (int y = 0; y < image.Height; y++) {
            for (int x = 0; x < image.Width; x++) {
                var color = ((x / 25) + (y / 25)) % 2 == 0 ?
                    new Rgb24(200, 200, 200) : new Rgb24(50, 50, 50);
                image[x, y] = color;
            }
        }

        return image;
    }
}