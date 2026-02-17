using ImgMzx;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImgMzxTests;

[TestClass]
public class AppMaskTest
{
    private readonly Mask _mask = new(AppConsts.FileMask);

    [TestMethod]
    public void Main()
    {
        var names = new[] {
            "gab_org", "gab_blur", "gab_bw", "gab_crop", "gab_exp", "gab_logo", "gab_noice", "gab_scale",
            "gab_sim1", "gab_sim2", "gab_face", "gab_r3", "gab_r10", "gab_r90", "gab_toside",
            "gab_nosim1", "gab_nosim2", "gab_nosim3", "gab_nosim4", "gab_nosim5"
        };

        var times = new double[names.Length];
        for (int i = 0; i < names.Length; i++) {
            var name = names[i];
            var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\{name}.jpg");
            Assert.IsNotNull(data);
            using var image = AppBitmap.GetImage(data, SixLabors.ImageSharp.Processing.RotateMode.None, SixLabors.ImageSharp.Processing.FlipMode.None);
            Assert.IsNotNull(image);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var mask = _mask.GetMask(image);
            sw.Stop();
            times[i] = sw.Elapsed.TotalMilliseconds;
            using var maskImg = new Image<L8>(MaskSize, MaskSize);
            for (int y = 0; y < MaskSize; y++)
            for (int x = 0; x < MaskSize; x++)
                maskImg[x, y] = new L8((byte)(mask[y * MaskSize + x] * 255f));
            maskImg.SaveAsPng($@"{AppContext.BaseDirectory}images\{name}_mask.png");

            // Overlay mask as blue transparency on resized image
            float scale = Math.Max((float)MaskSize / image.Width, (float)MaskSize / image.Height);
            int scaledW = (int)Math.Round(image.Width * scale);
            int scaledH = (int)Math.Round(image.Height * scale);
            using var resized = image.Clone(ctx => ctx
                .Resize(new ResizeOptions { Size = new Size(scaledW, scaledH), Mode = ResizeMode.Max })
                .Crop(new Rectangle((scaledW - MaskSize) / 2, (scaledH - MaskSize) / 2, MaskSize, MaskSize))
            );
            
            for (int y = 0; y < MaskSize; y++)
            for (int x = 0; x < MaskSize; x++)
            {
                float alpha = mask[y * MaskSize + x];
                var orig = resized[x, y];
                // Blend with blue: orig * (1-alpha) + (0,0,255) * alpha
                byte r = (byte)(orig.R * alpha);
                byte g = (byte)(orig.G * alpha);
                byte b = (byte)(orig.B * alpha + 255 * (1 - alpha));
                    resized[x, y] = new Rgb24(r, g, b);
            }
            resized.SaveAsPng($@"{AppContext.BaseDirectory}images\{name}_overlay.png");
        }
        var avg = times.Average();
        Console.WriteLine($"Average mask time: {avg:F2} ms");
    }
    private const int MaskSize = 256;
}
