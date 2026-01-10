using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImgMzx;

public partial class Images : IDisposable
{
    private void UpdatePanel(int index, string hash, long size, Image<Rgb24> image, string extension, DateTime? taken)
    {
        var img = GetImgFromDatabase(hash);
        if (img != null) {
            var panelX = new Panel {
                Hash = hash,
                Img = img.Value,
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
        if (imgX == null || imgY == null) {
            return;
        }

        var fx = imgX.Value.Family;
        var fy = imgY.Value.Family;
        if (fx == 0 && fy == 0) {
            var f = GetAvailableFamilyFromDatabase();
            UpdateImgInDatabase(hashX, AppConsts.AttributeFamily, f);
            UpdatePanel(0, hashX, panelX.Size, panelX.Image, panelX.Extension, panelX.Taken);

            UpdateImgInDatabase(hashY, AppConsts.AttributeFamily, f);
            UpdatePanel(1, hashY, panelY.Size, panelY.Image, panelY.Extension, panelY.Taken);

            InvalidateFamiliyInDatabase(f);
        }
        else if (fx != 0 && fy == 0) {
            UpdateImgInDatabase(hashY, AppConsts.AttributeFamily, fx);
            InvalidateFamiliyInDatabase(fx);
            UpdatePanel(1, hashY, panelY.Size, panelY.Image, panelY.Extension, panelY.Taken);
        }
        else if (fx == 0 && fy != 0) {
            UpdateImgInDatabase(hashX, AppConsts.AttributeFamily, fy);
            InvalidateFamiliyInDatabase(fy);
            UpdatePanel(0, hashX, panelX.Size, panelX.Image, panelX.Extension, panelX.Taken);
        }
        else { 
            var f = Math.Min(fx, fy);
            if (fx != f) {
                RenameFamilyInDatabase(fx, f);
                InvalidateFamiliyInDatabase(f);
            }

            if (fy != f) {
                RenameFamilyInDatabase(fy, f);
                InvalidateFamiliyInDatabase(f);
            }

            UpdatePanel(0, hashX, panelX.Size, panelX.Image, panelX.Extension, panelX.Taken);
            UpdatePanel(1, hashY, panelY.Size, panelY.Image, panelY.Extension, panelY.Taken);
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
