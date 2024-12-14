using System.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;

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
        if (!AppImgs.TryGet(hash, out img)) {
            return false;
        }

        Debug.Assert(img != null);
        var filename = AppFile.GetFileName(img.Name, AppConsts.PathHp);
        Debug.Assert(filename != null);
        imagedata = AppFile.ReadEncryptedFile(filename);
        if (imagedata == null) {
            return false;
        }

        extension = AppBitmap.GetExtension(imagedata);
        image = AppBitmap.GetImage(imagedata, img.RotateMode, img.FlipMode);
        if (image == null) {
            return false;
        }

        taken = AppBitmap.GetDateTaken(image);
        return true;
    }

    public static bool SetLeftPanel(string hash, IProgress<string>? progress)
    {
        progress?.Report($"Rendering left{AppConsts.CharEllipsis}");

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

        var imgpanel = new ImgPanel(
            img: img,
            size: imagedata.LongLength,
            image: image,
            extension: extension,
            taken: taken);
        _imgPanels[0] = imgpanel;
        return true;
    }

    public static bool SetRightPanel(string hash, IProgress<string>? progress)
    {
        progress?.Report($"Rendering right{AppConsts.CharEllipsis}");

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
            AppBitmap.Composite(_imgPanels[0]!.Image, image, out var imagexor);
            image.Dispose();
            image = imagexor;
        }

        var imgpanel = new ImgPanel(
            img: img,
            size: imagedata.LongLength,
            image: image,
            extension: extension,
            taken: taken);

        _imgPanels[1] = imgpanel;
        UpdateStatus(progress);
        return true;
    }

    public static bool UpdateRightPanel(IProgress<string>? progress)
    {
        return SetRightPanel(_imgPanels[1]!.Img.Hash, progress);
    }

    public static void Confirm()
    {
        var imgY = AppImgs.UpdateLastView(_imgPanels[1]!.Img.Hash);
        imgY = AppImgs.SetScore(_imgPanels[1]!.Img.Hash, _imgPanels[1]!.Img.Score + 1);

        var imgX = AppImgs.UpdateLastView(_imgPanels[0]!.Img.Hash);
        imgX = AppImgs.UpdateConfirmed(_imgPanels[0]!.Img.Hash);
        imgX = AppImgs.SetScore(_imgPanels[0]!.Img.Hash, _imgPanels[0]!.Img.Score + 1);
        if (imgX is { Verified: false }) {
            AppImgs.UpdateVerified(imgX.Hash);
        }

        if (imgX != null && imgY != null && imgY.Next.Equals(imgX.Hash)) {
            AppImgs.UpdateConfirmed(_imgPanels[1]!.Img.Hash);
        }
    }

    public static void DeleteLeft()
    {
        ImgMdf.Delete(_imgPanels[0]!.Img.Hash);
    }

    public static void DeleteRight(IProgress<string>? progress)
    {
        ImgMdf.Delete(_imgPanels[1]!.Img.Hash);
    }

    public static void Export(IProgress<string>? progress)
    {
        progress?.Report($"Exporting{AppConsts.CharEllipsis}");
        var filename = ImgMdf.Export(_imgPanels[0]!.Img.Hash);
        progress?.Report($"Exported to {filename}");
    }

    private static void UpdateStatus(IProgress<string>? progress)
    {
        var imgX = _imgPanels[0]!.Img;
        var nonzeroscore = AppImgs.NonZeroScore();
        var total = AppImgs.Count();
        progress?.Report($"{nonzeroscore}/{total} v{imgX.Distance:F4}");
    }
}