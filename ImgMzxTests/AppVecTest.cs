using System.Text;
using ImgMzx;

namespace ImgMzxTests;

[TestClass]
public class AppVecTest
{
    private static readonly StringBuilder sb = new();

    private static void GetVector(
        string basename, float[] basevector, string name, out float[]? vector)
    {
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\{name}.jpg");
        Assert.IsNotNull(data);
        using var image = AppBitmap.GetImage(data, SixLabors.ImageSharp.Processing.RotateMode.None, SixLabors.ImageSharp.Processing.FlipMode.None);
        Assert.IsNotNull(image);
        vector = AppVit.GetVector(image);
        Assert.IsNotNull(vector);
        var vdistance = AppVit.GetDistance(basevector, vector);
        sb.AppendLine($"{basename}-{name} = v{vdistance:F4}");
    }

    [TestMethod]
    public void Main()
    {
        var basename = "gab_org";
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\{basename}.jpg");
        Assert.IsNotNull(data);
        using var image = AppBitmap.GetImage(data, SixLabors.ImageSharp.Processing.RotateMode.None, SixLabors.ImageSharp.Processing.FlipMode.None);
        Assert.IsNotNull(image);
        var basevector = AppVit.GetVector(image);
        Assert.IsNotNull(basevector);

        Console.WriteLine($"Actual vector size: {basevector.Length}");

        GetVector(basename, basevector, "gab_blur", out var v_blur);
        GetVector(basename, basevector, "gab_bw", out var v_bw);
        GetVector(basename, basevector, "gab_crop", out var v_crop);
        GetVector(basename, basevector, "gab_exp", out var v_exp);
        GetVector(basename, basevector, "gab_face", out var v_face);
        GetVector(basename, basevector, "gab_flip", out var v_flip);
        GetVector(basename, basevector, "gab_logo", out var v_logo);
        GetVector(basename, basevector, "gab_noice", out var v_noice);
        GetVector(basename, basevector, "gab_r10", out var v_r10);
        GetVector(basename, basevector, "gab_r15", out var v_r15);
        GetVector(basename, basevector, "gab_r30", out var v_r30);
        GetVector(basename, basevector, "gab_r45", out var v_r45);
        GetVector(basename, basevector, "gab_r90", out var v_r90);
        GetVector(basename, basevector, "gab_resize", out var v_resize);
        GetVector(basename, basevector, "gab_sharp", out var v_sharp);

        Console.WriteLine(sb.ToString());
    }
}