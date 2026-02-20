using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;

namespace ImgMzx;

public partial class Images : IDisposable
{
    public Panel? GetPanel(int id)
    {
        return (id == 0 || id == 1) ? _imgPanels[id] : null;
    }

    private bool SetPanel(
        string hash,
        out byte[]? imagedata,
        out Img img,
        out Image<Rgb24>? image,
        out string extension,
        out DateTime? taken)
    {
        imagedata = null;
        image = null;
        extension = "xxx";
        taken = null;
        img = new Img(
                hash: string.Empty,
                rotateMode: RotateMode.None,
                flipMode: FlipMode.None,
                lastView: DateTime.MinValue,
                next: string.Empty,
                score: 0,
                lastCheck: DateTime.MinValue,
                distance: 0,
                history: string.Empty,
                images: this);

        if (!AppHash.IsValidHash(hash) || !ContainsImg(hash)) {
            return false;
        }

        imagedata = AppFile.ReadMex(hash);
        if (imagedata == null) {
            return false;
        }

        extension = AppBitmap.GetExtension(imagedata);
        img = GetImgFromDatabase(hash);
        if (string.IsNullOrEmpty(img.Hash)) {
            return false;
        }

        image = AppBitmap.GetImage(imagedata, img.RotateMode, img.FlipMode);
        if (image == null) {
            return false;
        }

        taken = AppBitmap.GetDateTaken(image);
        return true;
    }

    public bool SetLeftPanel(string hash)
    {
        if (_imgPanels[0]?.Image != null) {
            _imgPanels[0]?.Image.Dispose();
        }

        if (!SetPanel(hash,
                out var imagedata,
                out var img,
                out var image,
                out var extension,
                out var taken)) {
            return false;
        }

        Debug.Assert(imagedata != null);
        Debug.Assert(image != null);

        var imgpanel = new Panel {
            Hash = hash,
            Img = img,
            Size = imagedata.LongLength,
            Image = image,
            Extension = extension,
            Taken = taken
        };

        _imgPanels[0] = imgpanel;

        return true;
    }

    public bool SetRightPanel(string hash)
    {
        if (_imgPanels[1]?.Image != null) {
            _imgPanels[1]?.Image.Dispose();
        }

        if (!SetPanel(hash,
                out var imagedata,
                out var img,
                out var image,
                out var extension,
                out var taken)) {
            return false;
        }

        Debug.Assert(imagedata != null);
        Debug.Assert(image != null);

        if (ShowXOR) {
            AppBitmap.Composite(_imgPanels[0]!.Value.Image, image, out var imagexor);
            image.Dispose();
            image = imagexor;
        }

        var imgpanel = new Panel {
            Hash = hash,
            Img = img,
            Size = imagedata.LongLength,
            Image = image,
            Extension = extension,
            Taken = taken
        };

        _imgPanels[1] = imgpanel;

        return true;
    }
}
