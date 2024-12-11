using System.ComponentModel;
using System.Diagnostics;
using System.Text;
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
        img = Verify(img, _beam, null);

        Debug.Assert(img != null);
        _position = img.History.Length;
        if (!_beam[_position].Equals(img.Next)) {
            progress?.Report($"{img.Name}: reset horizon");
            img = AppImgs.SetHistoryNext(img.Hash, string.Empty, _beam[_position]);
        }

        Debug.Assert(img != null);

        var imgpanel = new ImgPanel(
            img: img,
            size: imagedata.LongLength,
            image: image,
            extension: extension,
            taken: taken);
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

        var sb = new StringBuilder();
        for (var i = 0; i <= _position; i++) {
            sb.Append(_beam[i][4]);
        }

        var history = sb.ToString();
        if (_position + 1 >= _beam.Count) {
            history = string.Empty;
        }

        var next = _beam[_position + 1];
        imgX = AppImgs.SetHistoryNext(imgX.Hash, history, next);
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

    public static void Export(IProgress<string>? progress)
    {
        progress?.Report($"Exporting{AppConsts.CharEllipsis}");
        var filename = ImgMdf.Export(_imgPanels[0].Img.Hash);
        progress?.Report($"Exported to {filename}");
    }

    private static void UpdateStatus(IProgress<string>? progress)
    {
        var imgX = _imgPanels[0].Img;
        var imgY = _imgPanels[1].Img;
        var vdistance = AppVit.GetDistance(imgX.Vector, imgY.Vector);
        var postion = _position;
        var vectorsize = _beam.Count;
        var nonzeroscore = AppImgs.NonZeroScore();
        progress?.Report($"{postion}/{vectorsize} n{nonzeroscore} v{vdistance:F4}");
    }

    public static Img? Verify(Img? img, List<string> beam, BackgroundWorker? backgroundworker)
    {
        Debug.Assert(img != null);

        var next = img.Next;
        var position = img.History.Length;
        var history = img.History;
        if (position > 0) {
            var sb = new StringBuilder();
            for (var i = 0; i < position; i++) {
                sb.Append(beam[i][4]);
            }

            history = sb.ToString();
            if (!history.Equals(img.History)) {
                position = 0;
                history = string.Empty;
                next = beam[0];
            }
        }

        if (!img.Next.Equals(beam[position])) {
            next = beam[position];
        }

        Debug.Assert(img != null);

        if (!history.Equals(img.History) || !next.Equals(img.Next)) {
            var imgnext = img.Next.Length <= 4 ? "0000" : img.Next[..4];
            backgroundworker?.ReportProgress(0, $"{img.Name}: {imgnext}[{img.History.Length}] {AppConsts.CharRightArrow} {next[..4]}[{position}]");
            img = AppImgs.SetHistoryNext(img.Hash, history, next);
        }

        return img;
    }
}