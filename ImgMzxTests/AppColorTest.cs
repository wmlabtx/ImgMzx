using ImgMzx;
using Microsoft.Data.Sqlite;

namespace ImgMzxTests;

[TestClass]
public class AppColorTest
{
    [TestMethod]
    public void RgbToOklab_KnownValues_ReturnsExpectedResults()
    {
        // Test black
        var (l1, a1, b1) = AppColor.RgbToOklab(0, 0, 0);
        Assert.AreEqual(0f, l1, 0.0001f, "l1");
        Assert.AreEqual(0f, a1, 0.0001f, "a1");
        Assert.AreEqual(0f, b1, 0.0001f, "b1");
        // Test white
        var (l2, a2, b2) = AppColor.RgbToOklab(255, 255, 255);
        Assert.AreEqual(1f, l2, 0.0001f, "l2");
        Assert.AreEqual(0f, a2, 0.0001f, "a2");
        Assert.AreEqual(0f, b2, 0.0001f, "b2");
        // Test red
        var (l3, a3, b3) = AppColor.RgbToOklab(255, 0, 0);
        Assert.AreEqual(0.6279f, l3, 0.0001f, "l3");
        Assert.AreEqual(0.2249f, a3, 0.0001f, "a3");
        Assert.AreEqual(0.1258f, b3, 0.0001f, "b3");
        // Test green
        var (l4, a4, b4) = AppColor.RgbToOklab(0, 255, 0);
        Assert.AreEqual(0.8664f, l4, 0.0001f, "l4");
        Assert.AreEqual(-0.2339f, a4, 0.0001f, "a4");
        Assert.AreEqual(0.1795f, b4, 0.0001f, "b4");
        // Test blue
        var (l5, a5, b5) = AppColor.RgbToOklab(0, 0, 255);
        Assert.AreEqual(0.4520f, l5, 0.0001f, "l5");
        Assert.AreEqual(-0.0324f, a5, 0.0001f, "a5");
        Assert.AreEqual(-0.3115f, b5, 0.0001f, "b5");
    }

    [TestMethod]
    public void FindMaximallyDistantLabCenters()
    {
        // Build all Lab values from RGB [0-255]
        var allLab = new List<(float L, float A, float B, byte R, byte G, byte Bl)>();
        for (int r = 0; r < 256; r++) {
            for (int g = 0; g < 256; g++) {
                for (int b = 0; b < 256; b++) {
                    var (l, a, lab_b) = AppColor.RgbToOklab((byte)r, (byte)g, (byte)b);
                    allLab.Add((l, a, lab_b, (byte)r, (byte)g, (byte)b));
                }
            }
        }

        Console.WriteLine($"Total Lab points: {allLab.Count}");

        // Greedy farthest-point sampling
        const int clusterCount = 256;
        var centers = new List<(float L, float A, float B, byte R, byte G, byte Bl)>(clusterCount) {
            // Start with black (0,0,0)
            allLab[0]
        };

        // Track min distance to any center for each point
        var minDistToCenter = new float[allLab.Count];
        Array.Fill(minDistToCenter, float.MaxValue);

        for (int i = 1; i < clusterCount; i++) {
            var lastCenter = centers[^1];

            // Update min distances with the last added center
            float maxMinDist = 0;
            int farthestIdx = 0;

            for (int j = 0; j < allLab.Count; j++) {
                var point = allLab[j];
                float distToLast = LabDistance(point.L, point.A, point.B, lastCenter.L, lastCenter.A, lastCenter.B);
                minDistToCenter[j] = Math.Min(minDistToCenter[j], distToLast);

                if (minDistToCenter[j] > maxMinDist) {
                    maxMinDist = minDistToCenter[j];
                    farthestIdx = j;
                }
            }

            centers.Add(allLab[farthestIdx]);
            Console.WriteLine($"Center {i}: RGB({allLab[farthestIdx].R},{allLab[farthestIdx].G},{allLab[farthestIdx].Bl}) -> Lab({allLab[farthestIdx].L:F4},{allLab[farthestIdx].A:F4},{allLab[farthestIdx].B:F4}) dist={maxMinDist:F4}");
        }

        // Reorder centers for uniform spacing between neighbors
        Console.WriteLine("\n=== Reordering for uniform spacing ===");
        var orderedCenters = ReorderForUniformSpacing(centers);

        // Output ordered centers with distances between neighbors
        Console.WriteLine("\n=== Ordered Centers with neighbor distances ===");
        var distances = new List<float>();
        for (int i = 0; i < orderedCenters.Count; i++) {
            var c = orderedCenters[i];
            float dist = 0;
            if (i > 0) {
                var prev = orderedCenters[i - 1];
                dist = MathF.Sqrt(LabDistance(c.L, c.A, c.B, prev.L, prev.A, prev.B));
                distances.Add(dist);
            }
            Console.WriteLine($"[{i,3}] {{ {c.R,3}, {c.G,3}, {c.Bl,3} }}, // dist={dist:F4} Lab({c.L:F4}, {c.A:F4}, {c.B:F4})");
        }

        // Statistics
        Console.WriteLine($"\n=== Statistics ===");
        Console.WriteLine($"Min distance:  {distances.Min():F4}");
        Console.WriteLine($"Max distance:  {distances.Max():F4}");
        Console.WriteLine($"Avg distance:  {distances.Average():F4} (maximize)");
        Console.WriteLine($"StdDev:        {StdDev(distances):F4} (minimize)");

        // Save to database
        SaveLabsToDatabase(orderedCenters);
        Console.WriteLine($"\nSaved {orderedCenters.Count} centers to database");

        Assert.AreEqual(clusterCount, orderedCenters.Count);
    }

    private static void SaveLabsToDatabase(List<(float L, float A, float B, byte R, byte G, byte Bl)> centers)
    {
        var connectionString = $"Data Source={AppConsts.FileDatabase};";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        // Delete all previous records
        using (var deleteCmd = connection.CreateCommand()) {
            deleteCmd.CommandText = $"DELETE FROM {AppConsts.TableLabs};";
            deleteCmd.ExecuteNonQuery();
        }

        // Insert new records
        using var transaction = connection.BeginTransaction();
        using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = $"INSERT INTO {AppConsts.TableLabs} ({AppConsts.AttributeL}, {AppConsts.AttributeA}, {AppConsts.AttributeB}) VALUES (@l, @a, @b);";
        
        var paramL = insertCmd.CreateParameter();
        paramL.ParameterName = "@l";
        insertCmd.Parameters.Add(paramL);
        
        var paramA = insertCmd.CreateParameter();
        paramA.ParameterName = "@a";
        insertCmd.Parameters.Add(paramA);
        
        var paramB = insertCmd.CreateParameter();
        paramB.ParameterName = "@b";
        insertCmd.Parameters.Add(paramB);

        foreach (var center in centers) {
            paramL.Value = center.L;
            paramA.Value = center.A;
            paramB.Value = center.B;
            insertCmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static List<(float L, float A, float B, byte R, byte G, byte Bl)> ReorderForUniformSpacing(
        List<(float L, float A, float B, byte R, byte G, byte Bl)> centers)
    {
        var n = centers.Count;
        var used = new bool[n];
        var result = new List<(float L, float A, float B, byte R, byte G, byte Bl)>(n);

        // Precompute distance matrix
        var distMatrix = new float[n, n];
        for (int i = 0; i < n; i++) {
            for (int j = i + 1; j < n; j++) {
                var d = LabDistance(centers[i].L, centers[i].A, centers[i].B,
                                    centers[j].L, centers[j].A, centers[j].B);
                distMatrix[i, j] = d;
                distMatrix[j, i] = d;
            }
        }

        // Find target distance (median of all distances)
        var allDists = new List<float>();
        for (int i = 0; i < n; i++) {
            for (int j = i + 1; j < n; j++) {
                allDists.Add(distMatrix[i, j]);
            }
        }
        allDists.Sort();
        float targetDist = allDists[allDists.Count / 2];
        Console.WriteLine($"Target distance (median): {MathF.Sqrt(targetDist):F4}");

        // Start with first center
        result.Add(centers[0]);
        used[0] = true;

        // Greedy: pick next point closest to target distance from current
        for (int step = 1; step < n; step++) {
            int lastIdx = centers.IndexOf(result[^1]);
            int bestIdx = -1;
            float bestDiff = float.MaxValue;

            for (int j = 0; j < n; j++) {
                if (used[j]) continue;
                float diff = MathF.Abs(distMatrix[lastIdx, j] - targetDist);
                if (diff < bestDiff) {
                    bestDiff = diff;
                    bestIdx = j;
                }
            }

            result.Add(centers[bestIdx]);
            used[bestIdx] = true;

            if (step % 50 == 0) {
                Console.WriteLine($"Reordering progress: {step}/{n}");
            }
        }

        return result;
    }

    private static float StdDev(List<float> values)
    {
        if (values.Count == 0) return 0;
        float avg = values.Average();
        float sumSq = values.Sum(v => (v - avg) * (v - avg));
        return MathF.Sqrt(sumSq / values.Count);
    }

    private static float LabDistance(float l1, float a1, float b1, float l2, float a2, float b2)
    {
        float dl = l1 - l2;
        float da = a1 - a2;
        float db = b1 - b2;
        return dl * dl + da * da + db * db;
    }
}
