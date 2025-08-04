using ImgMzx;

namespace ImgMzxTests;

[TestClass]
public class AppHashTest
{
    [TestMethod]
    public void Main()
    {
        var imgpath = $@"{AppContext.BaseDirectory}images\gab_org.jpg";
        var imagedata = AppFile.ReadFile(imgpath);
        Assert.IsNotNull(imagedata);
        var hash = AppHash.GetHash(imagedata);
        Assert.AreEqual("6458190E05C7C3F73F04E0F05008B0AA443E6E207916373CFFCBCDD5DA617557", hash);
    }
}