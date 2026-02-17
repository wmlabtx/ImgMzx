using System.Text;
using ImgMzx;

namespace ImgMzxTests;

[TestClass]
public class AppVitTest
{
    private static readonly StringBuilder sb = new();
    private readonly Vit _vit = new(AppConsts.FileVit);

    private void CompareVectors(
        string basename, float[] baseVectorAttn, string name)
    {
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\{name}.jpg");
        Assert.IsNotNull(data);
        using var image = AppBitmap.GetImage(data, SixLabors.ImageSharp.Processing.RotateMode.None, SixLabors.ImageSharp.Processing.FlipMode.None);
        Assert.IsNotNull(image);

        var vectorAttn = _vit.CalculateVector(image);

        var distAttn = Vit.ComputeDistance(baseVectorAttn, vectorAttn);


        sb.AppendLine($"{basename}-{name,-12} ATTN={distAttn:F4}");
    }

    [TestMethod]
    public void Main()
    {
        var basename = "gab_org";
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\{basename}.jpg");
        Assert.IsNotNull(data);
        using var image = AppBitmap.GetImage(data, SixLabors.ImageSharp.Processing.RotateMode.None, SixLabors.ImageSharp.Processing.FlipMode.None);
        Assert.IsNotNull(image);

        var baseVectorAttn = _vit.CalculateVector(image);

        sb.AppendLine("=== Similar images (lower is better) ===");
        CompareVectors(basename, baseVectorAttn, "gab_blur");
        CompareVectors(basename, baseVectorAttn, "gab_bw");
        CompareVectors(basename, baseVectorAttn, "gab_crop");
        CompareVectors(basename, baseVectorAttn, "gab_exp");
        CompareVectors(basename, baseVectorAttn, "gab_logo");
        CompareVectors(basename, baseVectorAttn, "gab_noice");
        CompareVectors(basename, baseVectorAttn, "gab_scale");
        CompareVectors(basename, baseVectorAttn, "gab_sim1");
        CompareVectors(basename, baseVectorAttn, "gab_sim2");
        CompareVectors(basename, baseVectorAttn, "gab_face");
        CompareVectors(basename, baseVectorAttn, "gab_r3");
        CompareVectors(basename, baseVectorAttn, "gab_r10");
        CompareVectors(basename, baseVectorAttn, "gab_r90");
        CompareVectors(basename, baseVectorAttn, "gab_toside");

        sb.AppendLine("\n=== Different images (higher is better) ===");
        CompareVectors(basename, baseVectorAttn, "gab_nosim1");
        CompareVectors(basename, baseVectorAttn, "gab_nosim2");
        CompareVectors(basename, baseVectorAttn, "gab_nosim3");
        CompareVectors(basename, baseVectorAttn, "gab_nosim4");
        CompareVectors(basename, baseVectorAttn, "gab_nosim5");

        File.WriteAllText($@"{AppContext.BaseDirectory}images\vit_comparison.txt", sb.ToString());
    }

    }