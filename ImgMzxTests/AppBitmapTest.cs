using System.Drawing.Imaging;
using ImgMzx;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace ImgMzxTests;

[TestClass]
public class AppBitmapTest
{
    [TestMethod]
    public void Main()
    {
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\gab_corrupted.jpg");
        Assert.IsNotNull(data);
        var m_corrupted = AppBitmap.GetImage(data);
        Assert.IsNull(m_corrupted);

        data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\num_corrupted.jpg");
        Assert.IsNotNull(data);
        m_corrupted = AppBitmap.GetImage(data);
        Assert.IsNotNull(m_corrupted);
        Assert.AreEqual(m_corrupted.Width, 956);
        Assert.AreEqual(m_corrupted.PixelType.BitsPerPixel, 24);

        data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\gab_org.jpg");
        Assert.IsNotNull(data);
        var format_org = SixLabors.ImageSharp.Image.DetectFormat(data);
        Assert.AreEqual(format_org.FileExtensions.First(), "jpg");
        var m_org = AppBitmap.GetImage(data);
        Assert.IsNotNull(m_org);
        Assert.AreEqual(m_org.Width, 2000);
        Assert.AreEqual(m_org.PixelType.BitsPerPixel, 24);
        Assert.IsNotNull(m_org.Metadata.DecodedImageFormat);
        Assert.AreEqual(m_org.Metadata.DecodedImageFormat.FileExtensions.First(), "jpg");
        using var bitmap = AppBitmap.GetBitmap(m_org);
        Assert.IsNotNull(bitmap);
        Assert.AreEqual(bitmap.Width, m_org.Width);
        var dt = AppBitmap.GetDateTaken(m_org);
        Assert.IsNotNull(dt);
        Assert.AreEqual(dt.Value.Hour, 10);

        var m_rt = AppBitmap.GetImage(data, RotateMode.Rotate90, FlipMode.Vertical);
        Assert.IsNotNull(m_rt);
        //Assert.AreEqual(m_org.Height, 2000);
        using var bitmaprt = AppBitmap.GetBitmap(m_rt);
        bitmaprt.Save($@"{AppContext.BaseDirectory}images\gab_rt.jpg", ImageFormat.Jpeg);

        data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\exif_nodt.jpg");
        Assert.IsNotNull(data);
        var m_exif = AppBitmap.GetImage(data);
        Assert.IsNotNull(m_exif);
        dt = AppBitmap.GetDateTaken(m_exif);
        Assert.IsNull(dt);

        data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\png.png");
        Assert.IsNotNull(data);
        var format_png = SixLabors.ImageSharp.Image.DetectFormat(data);
        Assert.AreEqual(format_png.FileExtensions.First(), "png");
        var m_png = AppBitmap.GetImage(data);
        Assert.IsNotNull(m_png);
        Assert.IsNotNull(m_png.Metadata.DecodedImageFormat);
        Assert.AreEqual(m_png.Metadata.DecodedImageFormat.FileExtensions.First(), "png");

        data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\num_8bbp.jpg");
        Assert.IsNotNull(data);
        var m_8bbp = AppBitmap.GetImage(data);
        Assert.IsNotNull(m_8bbp);
        Assert.AreEqual(m_8bbp.Width, 3000);
        Assert.AreEqual(m_8bbp.PixelType.BitsPerPixel, 24);

        data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\heic.heic");
        Assert.IsNotNull(data);
        var m_heic = AppBitmap.GetImage(data);
        Assert.IsNull(m_heic);

        data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\gab_logo.jpg");
        Assert.IsNotNull(data);
        var m_logo = AppBitmap.GetImage(data);
        Assert.IsNotNull(m_logo);
        AppBitmap.Composite(m_org, m_logo, out var m_xor);
        var fs = File.Create($@"{AppContext.BaseDirectory}images\gab_xor.jpg");
        m_xor.Save(fs, new JpegEncoder());
        fs.Close();
    }
}

