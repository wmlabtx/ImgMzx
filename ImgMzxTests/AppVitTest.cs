using ImgMzx;
using System.Text;

namespace ImgMzxTests;

[TestClass]
public class AppVitTest
{
    private static readonly StringBuilder sb = new();
    private readonly Vit _vit = new(AppConsts.FileVit);

    private float[] GetVector(string name) => GetVector(name, 384);

    private float[] GetVector(string name, int shortSide)
    {
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\{name}.jpg");
        Assert.IsNotNull(data);
        using var image = AppBitmap.GetImage(data, SixLabors.ImageSharp.Processing.RotateMode.None, SixLabors.ImageSharp.Processing.FlipMode.None);
        Assert.IsNotNull(image);
        return _vit.CalculateVector(image, shortSide);
    }

    [TestMethod]
    public void ComparisonTest()
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
            AppConsts.FileVit);
        var progressMessages = new List<string>();
        var progress = new Progress<string>(msg => progressMessages.Add(msg));
        images.Load(progress);
        var hashes = images.GetAllHashes().ToArray();

        const int count = 10;
        var distances = new List<float>();
        int skipped = 0;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < count; i++) {
            var hash = hashes[Random.Shared.Next(0, hashes.Length)];
            var storedVector = images.GetVector(hash).ToArray();
            if (storedVector.Length != AppConsts.VectorSize) {
                skipped++;
                continue;
            }

            var imgData = AppFile.ReadMex(hash);
            if (imgData == null) {
                skipped++;
                continue;
            }

            using var image = AppBitmap.GetImage(imgData);
            if (image == null) {
                skipped++;
                continue;
            }

            var computedVector = images.Vit.CalculateVector(image);
            var dist = Vit.ComputeDistance(storedVector, computedVector);
            distances.Add(dist);
            Console.WriteLine($"  {hash[..8]}… dist={dist:F4}  ({image.Width}x{image.Height})");
        }

        sw.Stop();

        if (distances.Count > 0) {
            Console.WriteLine();
            Console.WriteLine($"Compared: {distances.Count}  Skipped: {skipped}");
            Console.WriteLine($"Distance  min={distances.Min():F6}  max={distances.Max():F6}  avg={distances.Average():F6}");
        }

        Console.WriteLine($"Average vector time: {sw.Elapsed.TotalMilliseconds / count:F1} ms");
    }

    [TestMethod]
    public void ResolutionTest()
    {
        var simNames   = new[] { "gab_blur", "gab_bw", "gab_crop", "gab_exp", "gab_logo", "gab_noice",
                                 "gab_scale", "gab_xor", "gab_sim1", "gab_sim2", "gab_face",
                                 "gab_r3", "gab_r10", "gab_r90", "gab_toside" };
        var nosimNames = new[] { "gab_nosim1", "gab_nosim2", "gab_nosim3", "gab_nosim4", "gab_nosim5" };

        var output = new StringBuilder();
        output.AppendLine($"{"shortSide",-10} {"sim_avg",-10} {"nosim_avg",-10} {"sep",-8}");
        output.AppendLine(new string('-', 40));

        foreach (var shortSide in new[] { 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448, 512 }) {
            var baseVec   = GetVector("gab_org", shortSide);
            double simAvg   = simNames.Average(n   => Vit.ComputeDistance(baseVec, GetVector(n, shortSide)));
            double nosimAvg = nosimNames.Average(n => Vit.ComputeDistance(baseVec, GetVector(n, shortSide)));

            output.AppendLine($"{shortSide,-10} {simAvg,-10:F4} {nosimAvg,-10:F4} {nosimAvg - simAvg,-8:F4}");
        }

        Console.WriteLine(output.ToString());
    }
}