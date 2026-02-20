using ImgMzx;

namespace ImgMzxTests;

[TestClass]
public class AppBitmapTest
{
    [TestMethod]
    public void GetImage_ValidJpeg_ReturnsImage()
    {
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\gab_org.jpg");
        Assert.IsNotNull(data);
        using var image = AppBitmap.GetImage(data);
        Assert.IsNotNull(image);
        Assert.AreEqual(2000, image.Width);
        Assert.AreEqual(24, image.PixelType.BitsPerPixel);
    }

    [TestMethod]
    public void GetImage_Corrupted_ReturnsNull()
    {
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\gab_corrupted.jpg");
        Assert.IsNotNull(data);
        var image = AppBitmap.GetImage(data);
        Assert.IsNull(image);
    }
}