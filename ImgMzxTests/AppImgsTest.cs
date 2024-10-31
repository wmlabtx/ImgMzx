using ImgMzx;

namespace ImgMzxTests;

[TestClass]
public class AppImgsTest
{
    [TestMethod]
    public void Main()
    {
        AppImgs.Load(AppConsts.FileDatabase, null);
    }
}

