using ImgMzx;
using System.Text;

namespace ImgMzxTests;

[TestClass]
public class AppClassifierTests
{
    private static readonly StringBuilder sb = new();

    private static void GetVector(string basename, Florence2Result baseresult, string name, out Florence2Result result)
    {
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\{name}.jpg");
        Assert.IsNotNull(data);
        using var image = AppBitmap.GetImage(data);
        Assert.IsNotNull(image);
        result = AppFlorence.AnalyzeImage(image);
        Assert.IsNotNull(result);
        var vdistance = AppFlorence.GetDistance(baseresult.Vector, result.Vector);
        var tdistance = AppFlorence.GetDistance(baseresult.TextVector, result.TextVector);
        sb.AppendLine($"{basename}-{name} = v{vdistance:F4} = t{tdistance:F4} {result.Text}");
    }

    [TestMethod]
    public void Main()
    {
        var basename = "gab_org";
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\{basename}.jpg");
        Assert.IsNotNull(data);
        using var image = AppBitmap.GetImage(data);
        Assert.IsNotNull(image);

        var baseresult = AppFlorence.AnalyzeImage(image);

        GetVector(basename, baseresult, "gab_blur", out var v_blur);
        GetVector(basename, baseresult, "gab_bw", out var v_bw);
        GetVector(basename, baseresult, "gab_crop", out var v_crop);
        GetVector(basename, baseresult, "gab_exp", out var v_exp);
        GetVector(basename, baseresult, "gab_face", out var v_face);
        GetVector(basename, baseresult, "gab_flip", out var v_flip);
        GetVector(basename, baseresult, "gab_logo", out var v_logo);
        GetVector(basename, baseresult, "gab_noice", out var v_noice);
        GetVector(basename, baseresult, "gab_r10", out var v_r10);
        GetVector(basename, baseresult, "gab_r3", out var v_r3);
        GetVector(basename, baseresult, "gab_r90", out var v_r90);
        GetVector(basename, baseresult, "gab_toside", out var v_toside);
        GetVector(basename, baseresult, "gab_scale", out var v_scale);
        GetVector(basename, baseresult, "gab_xor", out var v_xor);
        GetVector(basename, baseresult, "gab_sim1", out var v_sim1);
        GetVector(basename, baseresult, "gab_sim2", out var v_sim2);
        GetVector(basename, baseresult, "gab_nosim1", out var v_nosim1);
        GetVector(basename, baseresult, "gab_nosim2", out var v_nosim2);
        GetVector(basename, baseresult, "gab_nosim3", out var v_nosim3);
        GetVector(basename, baseresult, "gab_nosim4", out var v_nosim4);
        GetVector(basename, baseresult, "gab_nosim5", out var v_nosim5);
        GetVector(basename, baseresult, "f2-1", out var v_f2_1);
        GetVector(basename, baseresult, "exif_nodt", out var v_exif_nodt);
        GetVector(basename, baseresult, "face", out var v_face_lowresolution);

        Assert.IsNotNull(v_f2_1);
        GetVector("f2-1", v_f2_1, "f2-2", out var v_f2_2);
        GetVector("f2-1", v_f2_1, "f2-3", out var v_f2_3);
        GetVector("f2-1", v_f2_1, "f2-4", out var v_f2_4);

        GetVector(basename, baseresult, "dalle1", out var v_dalle1);
        Assert.IsNotNull(v_dalle1);
        GetVector("dalle1", v_dalle1, "dalle2", out var v_dalle2);

        File.WriteAllText($@"{AppContext.BaseDirectory}images\distances.txt", sb.ToString());
    }
}