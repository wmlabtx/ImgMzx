using ImgMzx;

namespace ImgMzxTests;

[TestClass]
public class AppHashTest
{
    [TestMethod]
    public void GetHash_ValidData_Returns16CharLowercase()
    {
        var data = "test data"u8.ToArray();
        var hash = AppHash.GetHash(data);
        Assert.IsNotNull(hash);
        Assert.AreEqual(16, hash.Length);
        Assert.IsTrue(hash.All(c => char.IsLower(c) || char.IsDigit(c)));
    }

    [TestMethod]
    public void GetHash_IsDeterministic()
    {
        var data = "deterministic"u8.ToArray();
        var hash1 = AppHash.GetHash(data);
        var hash2 = AppHash.GetHash(data);
        Assert.AreEqual(hash1, hash2);
    }
}