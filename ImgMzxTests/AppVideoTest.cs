using FFMpegCore;
using ImgMzx;
using System.IO;

namespace ImgMzxTests;

[TestClass]
public class AppVideoTest
{
    private static readonly string VideoPath =
        Path.Combine(AppContext.BaseDirectory, "videos", "harvest_luna.mp4");

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        GlobalFFOptions.Configure(o => o.BinaryFolder = AppContext.BaseDirectory);
        AppVideoServer.Start();
    }

    [ClassCleanup]
    public static void ClassCleanup() => AppVideoServer.Stop();

    [TestMethod]
    public void IsVideo_Mp4Bytes_ReturnsTrue()
    {
        var data = File.ReadAllBytes(VideoPath);
        Assert.IsTrue(AppBitmap.IsVideo(data));
    }

    [TestMethod]
    public void IsVideo_JpegBytes_ReturnsFalse()
    {
        var data = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "images", "gab_org.jpg"));
        Assert.IsFalse(AppBitmap.IsVideo(data));
    }

    [TestMethod]
    public void GetExtension_Mp4Bytes_ReturnsMp4()
    {
        var data = File.ReadAllBytes(VideoPath);
        Assert.AreEqual("mp4", AppBitmap.GetExtension(data));
    }

    [TestMethod]
    public void Probe_Duration_IsPositive()
    {
        var info = FFProbe.Analyse(VideoPath);
        Console.WriteLine($"Duration: {info.Duration}");
        Assert.IsGreaterThan(0.0, info.Duration.TotalSeconds);
    }

    [TestMethod]
    public void Probe_VideoStream_HasDimensions()
    {
        var info = FFProbe.Analyse(VideoPath);
        var v = info.PrimaryVideoStream;
        Assert.IsNotNull(v);
        Console.WriteLine($"Video: {v.Width}x{v.Height} {v.CodecName} @ {v.FrameRate:F3} fps, bitrate {v.BitRate / 1000} kbps");
        Assert.IsGreaterThan(0, v.Width);
        Assert.IsGreaterThan(0, v.Height);
        Assert.IsFalse(string.IsNullOrEmpty(v.CodecName));
        Assert.IsGreaterThan(0.0, v.FrameRate);
    }

    [TestMethod]
    public void Probe_AudioStream_HasProperties()
    {
        var info = FFProbe.Analyse(VideoPath);
        var a = info.PrimaryAudioStream;
        if (a == null) {
            Console.WriteLine("No audio stream.");
            return;
        }

        Console.WriteLine($"Audio: {a.CodecName} {a.SampleRateHz} Hz, {a.Channels} ch, bitrate {a.BitRate / 1000} kbps");
        Assert.IsFalse(string.IsNullOrEmpty(a.CodecName));
        Assert.IsGreaterThan(0, a.SampleRateHz);
        Assert.IsGreaterThan(0, a.Channels);
    }

    [TestMethod]
    public void Probe_Format_IsExpected()
    {
        var info = FFProbe.Analyse(VideoPath);
        var fileSize = new FileInfo(VideoPath).Length;
        Console.WriteLine($"Format: {info.Format.FormatName}, size {fileSize / 1024} KB, bitrate {info.Format.BitRate / 1000:F0} kbps");
        Assert.IsTrue(info.Format.FormatName.Contains("mp4") || info.Format.FormatName.Contains("mov"));
        Assert.IsGreaterThan(0L, fileSize);
    }

    [TestMethod]
    public void GetVideoFirstFrame_ReturnsValidImage()
    {
        var data = File.ReadAllBytes(VideoPath);
        using var frame = AppBitmap.GetVideoFirstFrame(data);
        Assert.IsNotNull(frame);
        Console.WriteLine($"First frame: {frame.Width}x{frame.Height}");
        Assert.IsGreaterThan(0, frame.Width);
        Assert.IsGreaterThan(0, frame.Height);
    }
}
