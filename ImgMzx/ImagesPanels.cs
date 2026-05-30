using FFMpegCore;
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
        out string? videoPath,
        out int displayWidth,
        out int displayHeight)
    {
        imagedata = null;
        image = null;
        extension = "xxx";
        taken = null;
        videoPath = null;
        displayWidth = 0;
        displayHeight = 0;
        img = new Img(
                hash: string.Empty,
                rotateMode: RotateMode.None,
                flipMode: FlipMode.None,
                lastView: DateTime.MinValue,
                next: string.Empty,
                score: 0,
                lastCheck: DateTime.MinValue,
                distance: 0,
                family: 0,
                flag: 0,
                images: this);

        if (!AppHash.IsValidHash(hash) || !ContainsImg(hash)) {
            return false;
        }

        imagedata = AppFile.ReadMex(hash);
        if (imagedata == null) {
            return false;
        }

        var isVideo = AppBitmap.IsVideo(imagedata);
        extension = isVideo ? "mp4" : AppBitmap.GetExtension(imagedata);
        img = GetImgFromDatabase(hash);
        if (string.IsNullOrEmpty(img.Hash)) {
            return false;
        }

        if (isVideo) {
            videoPath = AppVideoServer.GetUrl(hash);
            try {
                var v = FFProbe.Analyse(videoPath).PrimaryVideoStream;
                displayWidth = v?.Width ?? 1920;
                displayHeight = v?.Height ?? 1080;
            }
            catch {
                displayWidth = 1920;
                displayHeight = 1080;
            }
            return true;
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
        if (oldLeft?.VideoPath != null)
            AppVideoServer.UncachePanel(oldLeft.Value.Hash);
        oldLeft?.Image?.Dispose();

        if (!SetPanel(hash,
                out var imagedata,
                out var img,
                out var image,
                out var extension,
                out var taken,
                out var videoPath,
                out var displayWidth,
                out var displayHeight)) {
            return false;
        }

        Debug.Assert(imagedata != null);

        if (videoPath != null)
            AppVideoServer.CachePanel(hash, imagedata);

        _imgPanels[0] = new Panel {
            Hash = hash,
            Img = img,
            Size = imagedata.LongLength,
            Image = image,
            Extension = extension,
            Taken = taken,
            VideoPath = videoPath,
            DisplayWidth = displayWidth,
            DisplayHeight = displayHeight
        };

        return true;
    }

    public bool SetRightPanel(string hash)
    {
        var oldRight = _imgPanels[1];
        if (oldRight?.VideoPath != null)
            AppVideoServer.UncachePanel(oldRight.Value.Hash);
        oldRight?.Image?.Dispose();

        if (!SetPanel(hash,
                out var imagedata,
                out var img,
                out var image,
                out var extension,
                out var taken,
                out var videoPath,
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

        if (videoPath != null)
            AppVideoServer.CachePanel(hash, imagedata);

        _imgPanels[1] = new Panel {
            Hash = hash,
            Img = img,
            Size = imagedata.LongLength,
            Image = image,
            Extension = extension,
            Taken = taken,
            VideoPath = videoPath,
            DisplayWidth = displayWidth,
            DisplayHeight = displayHeight
        };

        return true;
    }
}
