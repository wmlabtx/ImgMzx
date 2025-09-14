using Microsoft.EntityFrameworkCore.Metadata.Internal;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Policy;

namespace ImgMzx;

public static class AppPanels
{
    private static readonly ImgPanel?[] _imgPanels = {null, null};
    public static ImgPanel? GetImgPanel(int idPanel)
    {
        return _imgPanels[idPanel];
    }

    private static bool SetPanel(
        string hash, 
        out byte[]? imagedata, 
        out Img? img, 
        out SixLabors.ImageSharp.Image<Rgb24>? image,
        out string extension, 
        out DateTime? taken)
    {
        imagedata = null;
        img = null;
        image = null;
        extension = "xxx";
        taken = null;
        if (!AppDatabase.ContainsKey(hash)) {
            return false;
        }

        var filename = AppFile.GetFileName(hash, AppConsts.PathHp);
        Debug.Assert(filename != null);
        imagedata = AppFile.ReadEncryptedFile(filename);
        if (imagedata == null) {
            return false;
        }

        extension = AppBitmap.GetExtension(imagedata);
        img = AppDatabase.GetImg(hash);
        if (img == null) {
            return false;
        }

        image = AppBitmap.GetImage(imagedata, img.Value.RotateMode, img.Value.FlipMode);
        if (image == null) {
            return false;
        }

        taken = AppBitmap.GetDateTaken(image);
        return true;
    }

    public static bool SetLeftPanel(string hash, IProgress<string>? progress)
    {
        //progress?.Report($"Rendering left{AppConsts.CharEllipsis}");

        if (!SetPanel(hash,
                out var imagedata,
                out var img,
                out var image,
                out var extension,
                out var taken)) {
            return false;
        }

        Debug.Assert(imagedata != null);
        Debug.Assert(img != null);
        Debug.Assert(image != null);

        var imgpanel = new ImgPanel {
            Hash = hash,
            Img = img.Value,
            Size = imagedata.LongLength,
            Image = image,
            Extension = extension,
            Taken = taken
        };
        _imgPanels[0] = imgpanel;

        return true;
    }

    public static bool SetRightPanel(string hash, IProgress<string>? progress)
    {
        //progress?.Report($"Rendering right{AppConsts.CharEllipsis}");

        if (!SetPanel(hash,
                out var imagedata,
                out var img,
                out var image,
                out var extension,
                out var taken)) {
            return false;
        }

        Debug.Assert(imagedata != null);
        Debug.Assert(img != null);
        Debug.Assert(image != null);

        if (AppVars.ShowXOR) {
            AppBitmap.Composite(_imgPanels[0]!.Value.Image, image, out var imagexor);
            image.Dispose();
            image = imagexor;
        }

        var imgpanel = new ImgPanel {
            Hash = hash,
            Img = img.Value,
            Size = imagedata.LongLength,
            Image = image,
            Extension = extension,
            Taken = taken
        };

        _imgPanels[1] = imgpanel;

        return true;
    }

    public static bool UpdateRightPanel(IProgress<string>? progress)
    {
        return SetRightPanel(_imgPanels[1]!.Value.Hash, progress);
    }

    public static void Confirm(IProgress<string>? progress)
    {
        var hashX = _imgPanels[0]!.Value.Hash;
        var imgX = AppDatabase.GetImg(hashX);
        var hashY = _imgPanels[1]!.Value.Hash;
        var imgY = AppDatabase.GetImg(hashY);
        if (imgX != null && imgY != null) {
            AppDatabase.ImgUpdateProperty(hashX, AppConsts.AttributeScore, imgX.Value.Score + 1);
            AppDatabase.ImgUpdateProperty(hashY, AppConsts.AttributeScore, imgY.Value.Score + 1);
            AppDatabase.ImgUpdateProperty(hashX, AppConsts.AttributeLastView, DateTime.Now.Ticks);
            AppDatabase.ImgUpdateProperty(hashY, AppConsts.AttributeLastView, DateTime.Now.Ticks);
            AppDatabase.AddPair(hashX, hashY);

            progress?.Report($"Calculating{AppConsts.CharEllipsis}");
            (var next, var message) = AppDatabase.GetNext(hashX);
            progress?.Report(message);
            (next, message) = AppDatabase.GetNext(hashY);
            progress?.Report(message);
        }
    }

    public static void DeleteLeft(IProgress<string>? progress)
    {
        progress?.Report($"Calculating{AppConsts.CharEllipsis}");
        var hashX = _imgPanels[0]!.Value.Hash;
        var hashY = _imgPanels[1]!.Value.Hash;
        (var next, var message) = AppDatabase.GetNext(hashY, hashX);
        progress?.Report(message);
    }

    public static void DeleteRight(IProgress<string>? progress)
    {
        progress?.Report($"Calculating{AppConsts.CharEllipsis}");
        var hashX = _imgPanels[0]!.Value.Hash;
        var hashY = _imgPanels[1]!.Value.Hash;
        (var next, var message) = AppDatabase.GetNext(hashX, hashY);
        progress?.Report(message);
    }

    public static void Export(IProgress<string>? progress)
    {
        progress?.Report($"Exporting{AppConsts.CharEllipsis}");
        var filename0 = ImgMdf.Export(_imgPanels[0]!.Value.Hash);
        var filename1 = ImgMdf.Export(_imgPanels[1]!.Value.Hash);
        progress?.Report($"Exported to {filename0} and {filename1}");
    }
}