using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImgMzx;

public partial class Images : IDisposable
{
    private void UpdatePanel(int index, Panel panel)
    {
        var img = GetImgFromDatabase(panel.Hash);
        if (img.Hash.Length == AppConsts.HashLength) {
            panel.Img = img;
            _imgPanels[index] = panel;
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
            UpdatePanel(0, panelX);

            imgY.Family = f;
            UpdateImgInDatabase(hashY, AppConsts.AttributeFamily, f);
            UpdatePanel(1, panelY);
        }
        else if (fx != 0 && fy == 0) {
            imgY.Family = fx;
            UpdateImgInDatabase(hashY, AppConsts.AttributeFamily, fx);
            UpdatePanel(1, panelY);
        }
        else if (fx == 0 && fy != 0) {
            imgX.Family = fy;
            UpdateImgInDatabase(hashX, AppConsts.AttributeFamily, fy);
            UpdatePanel(0, panelX);
        }
        else {
            var f = Math.Min(fx, fy);
            if (fx != f) {
                imgX.Family = f;
                UpdateImgInDatabase(hashX, AppConsts.AttributeFamily, f);
                UpdatePanel(0, panelX);
            }

            if (fy != f) {
                imgY.Family = f;
                UpdateImgInDatabase(hashY, AppConsts.AttributeFamily, f);
                UpdatePanel(1, panelY);
            }
        }
    }

    public void FamilyRemove()
    {
        var panelX = _imgPanels[0]!.Value;
        UpdateImgInDatabase(panelX.Hash, AppConsts.AttributeFamily, 0);
        UpdatePanel(0, panelX);

        var panelY = _imgPanels[1]!.Value;
        UpdateImgInDatabase(panelY.Hash, AppConsts.AttributeFamily, 0);
        UpdatePanel(1, panelY);
    }
}
