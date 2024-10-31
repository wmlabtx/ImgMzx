using System.Text;
using ImgMzx;

namespace ImgMzxTests;

[TestClass]
public class AppFaceTest
{
    private static readonly StringBuilder sb = new();

    private static void GetVectorAndFaces(string basename, float[] basevector, float[] basefaces, string name, out float[]? vector, out float[]? faces)
    {
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\{name}.jpg");
        Assert.IsNotNull(data);
        using var image = AppBitmap.GetImage(data);
        Assert.IsNotNull(image);
        vector = AppVit.GetVector(image);
        Assert.IsNotNull(vector);
        faces = AppFace.GetVector(image);
        Assert.IsNotNull(faces);
        var vdistance = AppVit.GetDistance(basevector, vector);
        var fdistance = AppFace.GetDistance(basefaces, faces);
        sb.AppendLine($"{basename}-{name} = v{vdistance:F4} f{fdistance:F4}");
    }

    [TestMethod]
    public void Main()
    {
        var basename = "gab_org";
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\{basename}.jpg");
        Assert.IsNotNull(data);
        using var image = AppBitmap.GetImage(data);
        Assert.IsNotNull(image);
        var basevector = AppVit.GetVector(image);
        Assert.IsNotNull(basevector);
        var basefaces = AppFace.GetVector(image);
        Assert.IsNotNull(basefaces);

        GetVectorAndFaces(basename, basevector, basefaces, "gab_blur", out var v_blur, out var f_blur);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_bw", out var v_bw, out var f_bw);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_crop", out var v_crop, out var f_crop);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_exp", out var v_exp, out var f_exp);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_face", out var v_face, out var f_face);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_flip", out var v_flip, out var f_flip);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_logo", out var v_logo, out var f_logo);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_noice", out var v_noice, out var f_noice);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_r10", out var v_r10, out var f_r10);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_r3", out var v_r3, out var f_r3);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_r90", out var v_r90, out var f_r90);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_toside", out var v_toside, out var f_toside);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_scale", out var v_scale, out var f_scale);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_sim1", out var v_sim1, out var f_sim1);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_sim2", out var v_sim2, out var f_sim2);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_nosim1", out var v_nosim1, out var f_nosim1);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_nosim2", out var v_nosim2, out var f_nosim2);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_nosim3", out var v_nosim3, out var f_nosim3);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_nosim4", out var v_nosim4, out var f_nosim4);
        GetVectorAndFaces(basename, basevector, basefaces, "gab_nosim5", out var v_nosim5, out var f_nosim5);
        GetVectorAndFaces(basename, basevector, basefaces, "f2-1", out var v_f2_1, out var f_f2_1);

        Assert.IsNotNull(v_f2_1);
        Assert.IsNotNull(f_f2_1);
        GetVectorAndFaces("f2-1", v_f2_1, f_f2_1, "f2-2", out var v_f2_2, out var f_f2_2);
        GetVectorAndFaces("f2-1", v_f2_1, f_f2_1, "f2-3", out var v_f2_3, out var f_f2_3);
        GetVectorAndFaces("f2-1", v_f2_1, f_f2_1, "f2-4", out var v_f2_4, out var f_f2_4);

        GetVectorAndFaces(basename, basevector, basefaces, "dalle1", out var v_dalle1, out var f_dalle1);
        Assert.IsNotNull(v_dalle1);
        Assert.IsNotNull(f_dalle1);
        GetVectorAndFaces("dalle1", v_dalle1, f_dalle1, "dalle2", out var v_dalle2, out var f_dalle2);

        File.WriteAllText($@"{AppContext.BaseDirectory}images\distances.txt", sb.ToString());
    }
}