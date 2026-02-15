using System.Text;
using ImgMzx;

namespace ImgMzxTests;

[TestClass]
public class AppVitTest
{
    private static readonly StringBuilder sb = new();
    private readonly Vit _vit = new(AppConsts.FileVit);

    private void GetVector(
        string basename, float[] basevector, string name, out float[]? vector)
    {
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\{name}.jpg");
        Assert.IsNotNull(data);
        using var image = AppBitmap.GetImage(data, SixLabors.ImageSharp.Processing.RotateMode.None, SixLabors.ImageSharp.Processing.FlipMode.None);
        Assert.IsNotNull(image);
        vector = _vit.CalculateVector(image);
        Assert.IsNotNull(vector);
        var vdistance = Vit.ComputeDistance(basevector, vector);
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

        var basevector = _vit.CalculateVector(image);
        Assert.IsNotNull(basevector);

        GetVector(basename, basevector, "gab_blur", out var v_blur);
        GetVector(basename, basevector, "gab_bw", out var v_bw);
        GetVector(basename, basevector, "gab_crop", out var v_crop);
        GetVector(basename, basevector, "gab_exp", out var v_exp);
        GetVector(basename, basevector, "gab_face", out var v_face);
        GetVector(basename, basevector, "gab_flip", out var v_flip);
        GetVector(basename, basevector, "gab_logo", out var v_logo);
        GetVector(basename, basevector, "gab_noice", out var v_noice);
        GetVector(basename, basevector, "gab_r10", out var v_r10);
        GetVector(basename, basevector, "gab_r3", out var v_r3);
        GetVector(basename, basevector, "gab_r90", out var v_r90);
        GetVector(basename, basevector, "gab_toside", out var v_toside);
        GetVector(basename, basevector, "gab_scale", out var v_scale);
        GetVector(basename, basevector, "gab_sim1", out var v_sim1);
        GetVector(basename, basevector, "gab_sim2", out var v_sim2);
        GetVector(basename, basevector, "gab_nosim1", out var v_nosim1);
        GetVector(basename, basevector, "gab_nosim2", out var v_nosim2);
        GetVector(basename, basevector, "gab_nosim3", out var v_nosim3);
        GetVector(basename, basevector, "gab_nosim4", out var v_nosim4);
        GetVector(basename, basevector, "gab_nosim5", out var v_nosim5);
        GetVector(basename, basevector, "f2-1", out var v_f2_1);
        GetVector(basename, basevector, "exif_nodt", out var v_exif_nodt);

        Assert.IsNotNull(v_f2_1);
        GetVector("f2-1", v_f2_1, "f2-2", out var v_f2_2);
        GetVector("f2-1", v_f2_1, "f2-3", out var v_f2_3);
        GetVector("f2-1", v_f2_1, "f2-4", out var v_f2_4);

        GetVector(basename, basevector, "dalle1", out var v_dalle1);
        Assert.IsNotNull(v_dalle1);
        GetVector("dalle1", v_dalle1, "dalle2", out var v_dalle2);

        File.WriteAllText($@"{AppContext.BaseDirectory}images\vit_distances.txt", sb.ToString());
    }
}