using ImgMzx;

namespace ImgMzxTests;

[TestClass]
public class AppHashTest
{
    private const string ValidBase32Alphabet = "0123456789abcdefghijkmnpqrstuvxz";

    [TestMethod]
    public void GetHash_WithValidData_ReturnsCorrectFormat()
    {
        var data = "test data"u8.ToArray();
        var hash = AppHash.GetHash(data);
        Assert.IsNotNull(hash);
        Assert.AreEqual(16, hash.Length, "Hash should be exactly 16 characters");
        Assert.IsTrue(hash.All(ValidBase32Alphabet.Contains),
            "Hash should only contain valid base32 characters");
        Assert.DoesNotContain('l', hash, "Hash should not contain 'l'");
        Assert.DoesNotContain('o', hash, "Hash should not contain 'o'");
        Assert.IsTrue(hash.All(c => char.IsLower(c) || char.IsDigit(c)), "Hash should be lowercase");
    }

    [TestMethod]
    public void GetHash_WithEmptyArray_ReturnsValidHash()
    {
        var data = Array.Empty<byte>();
        var hash = AppHash.GetHash(data);
        Assert.IsNotNull(hash);
        Assert.AreEqual(16, hash.Length);
        Assert.IsTrue(AppHash.IsValidHash(hash));
        Assert.AreEqual("ueqc8gkqzge196pt", hash, "Hash of empty array should be consistent");
    }

    [TestMethod]
    public void GetHash_WithSingleByte_ReturnsValidHash()
    {
        var data = "*"u8.ToArray();
        var hash = AppHash.GetHash(data);
        Assert.IsNotNull(hash);
        Assert.AreEqual(16, hash.Length);
        Assert.IsTrue(AppHash.IsValidHash(hash));
    }

    [TestMethod]
    public void GetHash_WithLargeData_ReturnsValidHash()
    {
        var data = new byte[10 * 1024 * 1024]; // 10MB
        new Random(42).NextBytes(data);
        var hash = AppHash.GetHash(data);
        Assert.IsNotNull(hash);
        Assert.AreEqual(16, hash.Length);
        Assert.IsTrue(AppHash.IsValidHash(hash));
    }

    [TestMethod]
    public void GetHash_IsDeterministic()
    {
        var data = "deterministic test data"u8.ToArray();
        var hash1 = AppHash.GetHash(data);
        var hash2 = AppHash.GetHash(data);
        Assert.AreEqual(hash1, hash2, "Hash should be deterministic");
    }

    [TestMethod]
    public void GetHash_DifferentData_ProducesDifferentHashes()
    {
        var data1 = "first data set"u8.ToArray();
        var data2 = "second data set"u8.ToArray();
        var hash1 = AppHash.GetHash(data1);
        var hash2 = AppHash.GetHash(data2);
        Assert.AreNotEqual(hash1, hash2, "Different data should produce different hashes");
        Assert.AreEqual(16, hash1.Length);
        Assert.AreEqual(16, hash2.Length);
    }

    [TestMethod]
    public void GetHash_MinorDataChange_ProducesDifferentHash()
    {
        var data1 = "test data"u8.ToArray();
        var data2 = "test datA"u8.ToArray(); // Only last character different
        var hash1 = AppHash.GetHash(data1);
        var hash2 = AppHash.GetHash(data2);
        Assert.AreNotEqual(hash1, hash2, "Minor data change should produce different hash");
    }

    [TestMethod]
    public void GetHash_MultipleBlockSizes_ConsistentResults()
    {
        var random = new Random(42);
        var smallData = new byte[100];
        var mediumData = new byte[5 * 1024 * 1024]; // 5MB
        var largeData = new byte[12 * 1024 * 1024]; // 12MB
        
        random.NextBytes(smallData);
        random.NextBytes(mediumData);
        random.NextBytes(largeData);

        var smallHash = AppHash.GetHash(smallData);
        var mediumHash = AppHash.GetHash(mediumData);
        var largeHash = AppHash.GetHash(largeData);

        Assert.AreEqual(16, smallHash.Length);
        Assert.AreEqual(16, mediumHash.Length);
        Assert.AreEqual(16, largeHash.Length);
        
        Assert.IsTrue(AppHash.IsValidHash(smallHash));
        Assert.IsTrue(AppHash.IsValidHash(mediumHash));
        Assert.IsTrue(AppHash.IsValidHash(largeHash));
        
        Assert.AreNotEqual(smallHash, mediumHash);
        Assert.AreNotEqual(mediumHash, largeHash);
        Assert.AreNotEqual(smallHash, largeHash);
    }

    [TestMethod]
    public void GetHash_Performance_MeetsExpectations()
    {
        var data = new byte[5 * 1024 * 1024]; // 5MB typical image size
        new Random(42).NextBytes(data);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var hash = AppHash.GetHash(data);
        stopwatch.Stop();

        Assert.IsNotNull(hash);
        Assert.AreEqual(16, hash.Length);
        
        var throughputMBps = 5.0 / (stopwatch.ElapsedMilliseconds / 1000.0);
        Assert.IsGreaterThan(10, throughputMBps, $"Performance too slow: {throughputMBps:F1} MB/s");
    }

    [TestMethod]
    public void GetHash_CollisionResistance_StatisticalTest()
    {
        const int testCount = 100;
        var hashes = new HashSet<string>();
        var random = new Random(42);

        for (int i = 0; i < testCount; i++) {
            var data = new byte[random.Next(10, 1000)];
            random.NextBytes(data);
            
            var hash = AppHash.GetHash(data);
            Assert.DoesNotContain(hash, hashes, $"Collision detected at iteration {i}");
            hashes.Add(hash);
        }

        Assert.HasCount(testCount, hashes, "All hashes should be unique");
    }
}