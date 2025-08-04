using ImgMzx;

namespace ImgMzxTests;

[TestClass]
public class AppFileTest
{
    [TestMethod]
    public void ReadWriteFile()
    {
        var imgpath = $@"{AppContext.BaseDirectory}images\gab_org.jpg";
        var data = AppFile.ReadFile(imgpath);
        var imgpath_temp = imgpath + ".$$$";
        Assert.IsNotNull(data);
        AppFile.WriteFile(imgpath_temp, data);
        var rdata = AppFile.ReadFile(imgpath_temp);
        Assert.IsNotNull(rdata);
        Assert.AreEqual(data.Length, rdata.Length);
        Assert.AreEqual(data[100], rdata[100]);
    }

    [TestMethod]
    public void ReadWriteEncryptedFile()
    {
        var imgpath = $@"{AppContext.BaseDirectory}images\gab_org.jpg";
        var data = AppFile.ReadFile(imgpath);
        var imgpath_temp = imgpath + ".$$$";
        Assert.IsNotNull(data);
        AppFile.WriteEncryptedFile(imgpath_temp, data);
        var rdata = AppFile.ReadEncryptedFile(imgpath_temp);
        Assert.IsNotNull(rdata);
        Assert.AreEqual(data.Length, rdata.Length);
        Assert.AreEqual(data[100], rdata[100]);
    }

    [TestMethod]
    public void GetFilename()
    {
        const string name = "0f3912";
        var subdir = $@"{AppContext.BaseDirectory}subdir";
        var filename = AppFile.GetFileName(name, subdir);
        Assert.AreEqual(filename, $@"{subdir}\0\f\{name}");
    }

    [TestMethod]
    public void GetRecycledName()
    {
        const string name = "0f3912";
        const string ext = "jpeg";
        var filename = AppFile.GetRecycledName(name, ext, AppConsts.PathGbProtected, new DateTime(2024, 6, 13, 10, 23, 59));
        Assert.AreEqual(@$"{AppConsts.PathGbProtected}\2024-06-13\102359.{name}.jpeg", filename);
    }
}