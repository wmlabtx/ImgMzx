using ImgMzx;
using System.Security.Policy;
using System.Text;

namespace ImgMzxTests;

[TestClass]
public class AppVitTest
{
    private static readonly StringBuilder sb = new();
    private readonly Vit _vit = new(AppConsts.FileVit, AppConsts.FileMask);

    private float[] GetVector(string name)
    {
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\{name}.jpg");
        Assert.IsNotNull(data);
        using var image = AppBitmap.GetImage(data, SixLabors.ImageSharp.Processing.RotateMode.None, SixLabors.ImageSharp.Processing.FlipMode.None);
        Assert.IsNotNull(image);
        return _vit.CalculateVector(image);
    }

    [TestMethod]
    public void Main()
    {
        var gabs = new string[] { "gab_org", "gab_blur", "gab_bw", "gab_crop", "gab_exp", "gab_logo", "gab_noice", "gab_scale", "gab_xor",
            "gab_sim1", "gab_sim2", "gab_face", "gab_r3", "gab_r10", "gab_r90", "gab_toside",
            "gab_nosim1", "gab_nosim2", "gab_nosim3", "gab_nosim4", "gab_nosim5" };
        var fs = new string[] { "f2-1", "f2-2", "f2-3", "f2-4" };
        var output = new StringBuilder();
        var baseGab = gabs[0];
        var baseGabVector = GetVector(baseGab);

        output.AppendLine("Gabs comparison");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var vectorsGab = new float[gabs.Length][];
        for (int i = 0; i < gabs.Length; i++) {
            vectorsGab[i] = GetVector(gabs[i]);
        }
        sw.Stop();
        double avgGabVectorTime = sw.Elapsed.TotalMilliseconds / gabs.Length;

        sw.Restart();
        for (int i = 0; i < gabs.Length; i++) {
            var dist = Vit.ComputeDistance(baseGabVector, vectorsGab[i]);
            output.AppendLine($"{baseGab,-12} vs {gabs[i],-12} DIST={dist:F4}");
        }
        sw.Stop();
        double avgGabDistTime = sw.Elapsed.TotalMilliseconds / gabs.Length;

        var baseFs = fs[0];
        var baseFsVector = GetVector(baseFs);

        output.AppendLine("\nFs comparison");
        sw.Restart();
        var vectorsFs = new float[fs.Length][];
        for (int i = 0; i < fs.Length; i++) {
            vectorsFs[i] = GetVector(fs[i]);
        }
        sw.Stop();
        double avgFsVectorTime = sw.Elapsed.TotalMilliseconds / fs.Length;

        sw.Restart();
        for (int i = 0; i < fs.Length; i++) {
            var dist = Vit.ComputeDistance(baseFsVector, vectorsFs[i]);
            output.AppendLine($"{baseFs,-8} vs {fs[i],-8} DIST={dist:F4}");
        }
        sw.Stop();
        double avgFsDistTime = sw.Elapsed.TotalMilliseconds / fs.Length;

        output.AppendLine();
        output.AppendLine($"Average vector extraction time (gabs): {avgGabVectorTime:F2} ms");
        output.AppendLine($"Average distance computation time (gabs): {avgGabDistTime:F4} ms");
        output.AppendLine($"Average vector extraction time (fs): {avgFsVectorTime:F2} ms");
        output.AppendLine($"Average distance computation time (fs): {avgFsDistTime:F4} ms");

        Console.WriteLine(output.ToString());
    }

    [TestMethod]
    public void GetVectorTest()
    {
        using var images = new Images(
            AppConsts.FileDatabase,
            AppConsts.FileVit,
            AppConsts.FileMask);
        var progressMessages = new List<string>();
        var progress = new Progress<string>(msg => progressMessages.Add(msg));
        images.Load(progress);
        var hashes = images.GetAllHashes().ToArray();
        var irandom = Random.Shared.Next(0, hashes.Length);
        var hash = hashes[irandom];
        var img = images.GetImgFromDatabase(hash);
        var imgData = AppFile.ReadMex(hash);
        if (imgData != null) {
            using var image = AppBitmap.GetImage(imgData);
            if (image != null) {
                var newVector = images.Vit.CalculateVector(image);
                var vit = images.Vit;
                var vector = vit.CalculateVector(image, isDebug: true);
                Console.WriteLine($"Debug images for hash {hash} saved to debug_output/");
            }
        }
    }
}