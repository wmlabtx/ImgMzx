using System.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;

namespace ImgMzx;

public static class AppPanels
{
    private static readonly ImgPanel[] _imgPanels = new ImgPanel[2];
    public static ImgPanel GetImgPanel(int idPanel)
    {
        return _imgPanels[idPanel];
    }

    private static List<string> _beam = new();
    private static int _position;

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

        progress?.Report($"Getting vector{AppConsts.CharEllipsis}");

        _beam = AppImgs.GetBeam(img);
        if (img is { Counter: 0, Horizon.Length: > 0 }) {
            img = AppImgs.SetHorizonCounter(img.Hash, string.Empty, 0);
        }
        else {
            if (img.Counter > 0) {
                if (img.Counter >= _beam.Count) {
                    img = AppImgs.SetHorizonCounter(img.Hash, string.Empty, 0);
                }
                else {
                    var horizon = _beam[img.Counter - 1];
                    if (!horizon.Equals(img.Horizon)) {
                        img = AppImgs.SetHorizonCounter(img.Hash, string.Empty, 0);
                    }
                }
            }
        }

        Debug.Assert(img != null);
        _position = img.Counter;

        var familysize = AppImgs.GetFamilySize(img.Family);
        var imgpanel = new ImgPanel(
            img: img,
            size: imagedata.LongLength,
            image: image,
            extension: extension,
            taken: taken,
            familysize: familysize);
        _imgPanels[0] = imgpanel;
        return true;
    }

    public static bool UpdateRightPanel(IProgress<string>? progress)
    {
        return SetRightPanel(_beam[_position][4..], progress);
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
            AppBitmap.Composite(_imgPanels[0].Image, image, out var imagexor);
            image.Dispose();
            image = imagexor;
        }

        var familysize = AppImgs.GetFamilySize(img.Family);
        var imgpanel = new ImgPanel(
            img: img,
            size: imagedata.LongLength,
            image: image,
            extension: extension,
            taken: taken,
            familysize: familysize);

        _imgPanels[1] = imgpanel;
        UpdateStatus(progress);
        return true;
    }

    public static string GetRight()
    {
        return _beam[_position][4..];
    }

    public static void MoveRight(IProgress<string>? progress)
    {
        if (_position + 1 < _beam.Count) {
            _position++;
        }

        SetRightPanel(_beam[_position][4..], progress);
    }

    public static void MoveLeft(IProgress<string>? progress)
    {
        if (_position - 1 >= 0) {
            _position--;
        }

        SetRightPanel(_beam[_position][4..], progress);
    }

    public static void MoveToTheFirst(IProgress<string>? progress)
    { 
        _position = 0;
        SetRightPanel(_beam[_position][4..], progress);
    }

    public static void MoveToTheLast(IProgress<string>? progress)
    {
        _position = _beam.Count - 1;
        SetRightPanel(_beam[_position][4..], progress);
    }

    public static void Confirm()
    {
        AppImgs.UpdateLastView(_imgPanels[1].Img.Hash);
        AppImgs.SetScore(_imgPanels[1].Img.Hash, _imgPanels[1].Img.Score + 1);
        var imgX = AppImgs.UpdateLastView(_imgPanels[0].Img.Hash);
        Debug.Assert(imgX != null);
        imgX = AppImgs.SetScore(imgX.Hash, imgX.Score + 1);
        Debug.Assert(imgX != null);
        var horizon = _beam[_position];
        var counter = _position + 1;
        if (counter >= _beam.Count) {
            horizon = string.Empty;
            counter = 0;
        }

        imgX = AppImgs.SetHorizonCounter(imgX.Hash, horizon, counter);
        Debug.Assert(imgX != null);
        if (!imgX.Verified) {
            AppImgs.UpdateVerified(imgX.Hash);
        }
    }

    public static void DeleteLeft()
    {
        ImgMdf.Delete(_imgPanels[0].Img.Hash);
        AppImgs.UpdateLastView(_imgPanels[1].Img.Hash);
    }

    public static void DeleteRight(IProgress<string>? progress)
    {
        var imgX = AppImgs.UpdateLastView(_imgPanels[0].Img.Hash);
        Debug.Assert(imgX != null);
        _imgPanels[0].Img = imgX;
        ImgMdf.Delete(_imgPanels[1].Img.Hash);

        _beam.RemoveAt(_position);
        if (_position >= _beam.Count) {
            _position = _beam.Count - 1;
        }

        if (progress != null) {
            SetRightPanel(_beam[_position][4..], progress);
        }
    }

    private static void UpdateStatus(IProgress<string>? progress)
    {
        var imgX = _imgPanels[0].Img;
        var imgY = _imgPanels[1].Img;
        var vdistance = AppVit.GetDistance(imgX.Vector, imgY.Vector);
        var postion = _position;
        var vectorsize = _beam.Count;
        progress?.Report($"{postion}/{vectorsize} v{vdistance:F4}");
    }

    private static void UpdateFamiliesOnPanels(Img imgX, Img imgY)
    {
        _imgPanels[0].Img = imgX;
        _imgPanels[0].FamilySize = AppImgs.GetFamilySize(imgX.Family);
        _imgPanels[1].Img = imgY;
        _imgPanels[1].FamilySize = AppImgs.GetFamilySize(imgY.Family);
    }

    public static void CombineToFamily()
    {
        var imgX = _imgPanels[0].Img;
        var imgY = _imgPanels[1].Img;
        if (imgX.Family.Equals(imgY.Family)) {
            return;
        }

        var sizeX = AppImgs.GetFamilySize(imgX.Family);
        var sizeY = AppImgs.GetFamilySize(imgY.Family);

        if (sizeX >= sizeY) {
            AppImgs.RenameFamily(imgY.Family, imgX.Family);
        }
        else {
            AppImgs.RenameFamily(imgX.Family, imgY.Family);
        }

        if (!AppImgs.TryGet(imgX.Hash, out imgX)) {
            return;
        }

        if (!AppImgs.TryGet(imgY.Hash, out imgY)) {
            return;
        }

        Debug.Assert(imgX != null);
        Debug.Assert(imgY != null);
        UpdateFamiliesOnPanels(imgX, imgY);
    }

    public static void DetachFromFamily()
    {
        var imgX = _imgPanels[0].Img;
        var imgY = _imgPanels[1].Img;
        if (!imgY.Family.Equals(imgY.Family)) {
            return;
        }

        if (!imgY.Hash.Equals(imgY.Family)) {
            imgY = AppImgs.SetFamily(imgY.Hash, imgY.Hash);
        }
        else {
            if (!imgX.Hash.Equals(imgX.Family)) {
                imgX = AppImgs.SetFamily(imgX.Hash, imgX.Hash);
            }
        }

        Debug.Assert(imgX != null);
        Debug.Assert(imgY != null);
        UpdateFamiliesOnPanels(imgX, imgY);
    }
}