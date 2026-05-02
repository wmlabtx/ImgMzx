using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImgMzx;

public partial class Images : IDisposable
{
    private void UpdatePanel(int index, string hash, long size, Image<Rgb24> image, string extension, DateTime? taken)
    {
        var img = GetImgFromDatabase(hash);
        if (img.Hash.Length == AppConsts.HashLength) {
            var panelX = new Panel {
                Hash = hash,
                Img = img,
                Size = size,
                Image = image,
                Extension = extension,
                Taken = taken
            };

            _imgPanels[index] = panelX;
        }
    }

    public void FamilyAdd()
    {
        var panelX = _imgPanels[0]!.Value;
        var hashX = panelX.Hash;
        var imgX = GetImgFromDatabase(hashX);
        var panelY = _imgPanels[1]!.Value;
        var hashY = panelY.Hash;
        var imgY = GetImgFromDatabase(hashY);
        if (imgX.Hash.Length == 0 || imgY.Hash.Length == 0) {
            return;
        }

        var fx = imgX.Family;
        var fy = imgY.Family;
        if (fx == 0 && fy == 0) {
            var f = GetAvailableFamilyFromDatabase();
            imgX.Family = f;
            UpdateImgInDatabase(hashX, AppConsts.AttributeFamily, f);
            UpdatePanel(0, hashX, panelX.Size, panelX.Image, panelX.Extension, panelX.Taken);

            imgX.Family = f;
            UpdateImgInDatabase(hashY, AppConsts.AttributeFamily, f);
            UpdatePanel(1, hashY, panelY.Size, panelY.Image, panelY.Extension, panelY.Taken);
        }
        else if (fx != 0 && fy == 0) {
            imgY.Family = fx;
            UpdateImgInDatabase(hashY, AppConsts.AttributeFamily, fx);
            UpdatePanel(1, hashY, panelY.Size, panelY.Image, panelY.Extension, panelY.Taken);
        }
        else if (fx == 0 && fy != 0) {
            imgX.Family = fy;
            UpdateImgInDatabase(hashX, AppConsts.AttributeFamily, fy);
            UpdatePanel(0, hashX, panelX.Size, panelX.Image, panelX.Extension, panelX.Taken);
        }
        else {
            var f = Math.Min(fx, fy);
             if (fx != f) {
                imgX.Family = f;
                UpdateImgInDatabase(hashX, AppConsts.AttributeFamily, f);
                UpdatePanel(0, hashX, panelX.Size, panelX.Image, panelX.Extension, panelX.Taken);
            }

            if (fy != f) {
                imgY.Family = f;
                UpdateImgInDatabase(hashY, AppConsts.AttributeFamily, f);
                UpdatePanel(1, hashY, panelY.Size, panelY.Image, panelY.Extension, panelY.Taken);
            }
        }
    }

    public void FamilyRemove()
    {
        var panelX = _imgPanels[0]!.Value;
        var hashX = panelX.Hash;
        UpdateImgInDatabase(hashX, AppConsts.AttributeFamily, 0);
        UpdatePanel(0, hashX, panelX.Size, panelX.Image, panelX.Extension, panelX.Taken);

        var panelY = _imgPanels[1]!.Value;
        var hashY = panelY.Hash;
        UpdateImgInDatabase(hashY, AppConsts.AttributeFamily, 0);
        UpdatePanel(1, hashY, panelY.Size, panelY.Image, panelY.Extension, panelY.Taken);
    }
}