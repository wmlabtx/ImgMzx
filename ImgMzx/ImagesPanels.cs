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
        out DateTime? taken,
        out int displayWidth,
        out int displayHeight)
    {
        imagedata = null;
        image = null;
        extension = "xxx";
        taken = null;
        displayWidth = 0;
        displayHeight = 0;
        img = new Img(
                hash: string.Empty,
                rotateMode: RotateMode.None,
                flipMode: FlipMode.None,
                lastView: DateTime.MinValue,
                history: string.Empty,
                rate: 0,
                distance: 0.0f,
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
        displayWidth = image.Width;
        displayHeight = image.Height;
        return true;
    }

    public bool SetLeftPanel(string hash)
    {
        var oldLeft = _imgPanels[0];
        oldLeft?.Image?.Dispose();

        if (!SetPanel(hash,
                out var imagedata,
                out var img,
                out var image,
                out var extension,
                out var taken,
                out var displayWidth,
                out var displayHeight)) {
            return false;
        }

        Debug.Assert(imagedata != null);

        _imgPanels[0] = new Panel {
            Hash = hash,
            Img = img,
            Size = imagedata.LongLength,
            Image = image,
            Extension = extension,
            Taken = taken,
            DisplayWidth = displayWidth,
            DisplayHeight = displayHeight
        };

        return true;
    }

    public bool SetRightPanel(string hash)
    {
        var oldRight = _imgPanels[1];
        oldRight?.Image?.Dispose();

        if (!SetPanel(hash,
                out var imagedata,
                out var img,
                out var image,
                out var extension,
                out var taken,
                out var displayWidth,
                out var displayHeight)) {
            return false;
        }

        Debug.Assert(imagedata != null);

        if (image != null && ShowXOR && _imgPanels[0]?.Image != null) {
            AppBitmap.Composite(_imgPanels[0]!.Value.Image!, image, out var imagexor);
            image.Dispose();
            image = imagexor;
        }

        _imgPanels[1] = new Panel {
            Hash = hash,
            Img = img,
            Size = imagedata.LongLength,
            Image = image,
            Extension = extension,
            Taken = taken,
            DisplayWidth = displayWidth,
            DisplayHeight = displayHeight
        };

        return true;
    }

    private void UpdatePanel(int index, Panel panel)
    {
        var img = GetImgFromDatabase(panel.Hash);
        if (img.Hash.Length == AppConsts.HashLength) {
            panel.Img = img;
            _imgPanels[index] = panel;
        }
    }

    public void Rate(int index)
    {
        var panel = _imgPanels[index]!.Value;
        var hash = panel.Hash;
        var img = GetImgFromDatabase(hash);
        if (img.Hash.Length == 0) {
            return;
        }

        img.Rate = 1 - img.Rate;

        UpdateImgInDatabase(hash, AppConsts.AttributeRate, img.Rate);
        UpdatePanel(index, panel);
    }
}
