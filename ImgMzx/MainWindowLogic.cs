using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using SixLabors.ImageSharp.Processing;
using System.Runtime.Versioning;

namespace ImgMzx;

public sealed partial class MainWindow
{
    private double _picsMaxWidth;
    private double _picsMaxHeight;
    private double _labelMaxHeight;

    [SupportedOSPlatform("windows6.1")]
    private readonly NotifyIcon _notifyIcon = new();
    private readonly Images _images = new(AppConsts.FileDatabase, AppConsts.FileVit);
    
    private Progress<string>? _progress;

    [SupportedOSPlatform("windows6.1")]
    private async void WindowLoaded()
    {
        BoxLeft.MouseDown += PictureLeftBoxMouseClick;
        BoxRight.MouseDown += PictureRightBoxMouseClick;
        VideoLeft.MediaEnded += (s, e) => { VideoLeft.Position = TimeSpan.Zero; VideoLeft.Play(); };
        VideoRight.MediaEnded += (s, e) => { VideoRight.Position = TimeSpan.Zero; VideoRight.Play(); };
        AppVideoServer.Start();

        LabelLeft.MouseDown += ButtonLeftNextMouseClick;
        LabelRight.MouseDown += ButtonRightNextMouseClick;

        Left = SystemParameters.WorkArea.Left + AppConsts.WindowMargin;
        Top = SystemParameters.WorkArea.Top + AppConsts.WindowMargin;
        Width = SystemParameters.WorkArea.Width - AppConsts.WindowMargin * 2;
        Height = SystemParameters.WorkArea.Height - AppConsts.WindowMargin * 2;
        Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - AppConsts.WindowMargin - Width) / 2;
        Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - AppConsts.WindowMargin - Height) / 2;

        _picsMaxWidth = Grid.ActualWidth;
        _labelMaxHeight = LabelLeft.ActualHeight;
        _picsMaxHeight = Grid.ActualHeight - _labelMaxHeight;

        
        _notifyIcon.Icon = new Icon(@"app.ico");
        _notifyIcon.Visible = false;
        _notifyIcon.DoubleClick +=
            delegate
            {
                Show();
                WindowState = WindowState.Normal;
                _notifyIcon.Visible = false;
                RedrawCanvas();
            };

        _progress = new Progress<string>(message => Status.Text = message);

        await Task.Run(() => { _images.Load(_progress); }).ConfigureAwait(true);

        foreach (var item in FamilyMenuItem.Items) {
            if (item is System.Windows.Controls.MenuItem menuItem) {
                var mi = item as System.Windows.Controls.MenuItem;
                if (mi != null) {
                    if (mi.Tag is string tag) {
                        var familyId = int.Parse(tag);
                        var familyDescription = _images.GetFamilyDescription(familyId);
                        mi.Header = familyDescription;
                    }
                }
            }
        }

        await Task.Run(() => { _images.Find(null, _progress); }).ConfigureAwait(true);

        DrawCanvas();
    }

    [SupportedOSPlatform("windows6.1")]
    private void OnStateChanged()
    {
        if (WindowState != WindowState.Minimized) {
            return;
        }

        Hide();
        _notifyIcon.Visible = true;
    }

    private async void ImportClick()
    {
        DrawCanvas();
        await Task.Run(() => { _images.Import(_progress); }).ConfigureAwait(true);
        EnableElements();
    }

    private void ExportClick()
    {
        ImgPanelExport();
    }

    private void PictureLeftBoxMouseClick()
    {
        ImgPanelDeleteLeft();
    }

    private void PictureRightBoxMouseClick()
    {
        ImgPanelDeleteRight();
    }

    private async void ButtonLeftNextMouseClick()
    {
        DisableElements();
        try {
            await Task.Run(() =>
            {
                _images.Confirm(_progress);
                _images.Find(null, _progress);
            });

            DrawCanvas();
        }
        catch (Exception) {
        }
        finally {
            EnableElements();
        }
    }

    private async void ButtonRightNextMouseClick()
    {
        DisableElements();
        await Task.Run(() => { _images.Confirm(_progress); }).ConfigureAwait(true);
        var hashX = _images.GetPanel(0)!.Value.Hash;
        await Task.Run(() => { _images.Find(hashX, _progress); }).ConfigureAwait(true);
        DrawCanvas();
        EnableElements();
    }

    private void DisableElements()
    {
        ElementsEnable(false);
    }

    private void EnableElements()
    {
        ElementsEnable(true);
    }

    private void ElementsEnable(bool enabled)
    {
        foreach (System.Windows.Controls.MenuItem item in Menu.Items) {
            item.IsEnabled = enabled;
        }

        Status.IsEnabled = enabled;
        BoxLeft.IsEnabled = enabled;
        BoxRight.IsEnabled = enabled;
        VideoLeft.IsEnabled = enabled;
        VideoRight.IsEnabled = enabled;
        LabelLeft.IsEnabled = enabled;
        LabelRight.IsEnabled = enabled;
    }

    private void DrawCanvas()
    {
        var videoBoxes = new[] { VideoLeft, VideoRight };
        foreach (var vb in videoBoxes) {
            vb.Stop();
            vb.Source = null;
        }

        var panels = new Panel?[2];
        panels[0] = _images.GetPanel(0);
        panels[1] = _images.GetPanel(1);
        if (panels[0] == null || panels[1] == null) {
            return;
        }

        var pBoxes = new[] { BoxLeft, BoxRight };
        var pLabels = new[] { LabelLeft, LabelRight };

        for (var index = 0; index < 2; index++) {
            var ix = panels[index]!.Value;

            if (ix.VideoPath != null) {
                pBoxes[index].Visibility = Visibility.Collapsed;
                videoBoxes[index].Visibility = Visibility.Visible;
                videoBoxes[index].Source = new Uri(ix.VideoPath);
                videoBoxes[index].Play();
            } else {
                pBoxes[index].Visibility = Visibility.Visible;
                videoBoxes[index].Visibility = Visibility.Collapsed;
                pBoxes[index].Source = AppBitmap.GetImageSource(ix.Image!);
            }

            var sb = new StringBuilder();
            sb.Append($"{ix.Hash[..4]}.{ix.Extension}");

            if (ix.Img.Score > 0) {
                sb.Append($" [{ix.Img.Score}]");
            }

            if (ix.Img.Family > 0) {
                var fsize = _images.GetFamilySizeFromDatabase(ix.Img.Family);
                var familyDescription = _images.GetFamilyDescription(ix.Img.Family);
                sb.Append($" [{familyDescription}:{fsize}]");
            }

            sb.AppendLine();

            sb.Append($"{Helper.SizeToString(ix.Size)} ");
            sb.Append($" ({ix.DisplayWidth}x{ix.DisplayHeight})");
            sb.AppendLine();

            sb.Append($" {Helper.TimeIntervalToString(DateTime.Now.Subtract(ix.Img.LastView))} ago ");
            var dateTime = ix.Taken;
            if (dateTime != null) {
                sb.Append($" [{dateTime.Value.ToShortDateString()}]");
            }

            var meta = ix.Image != null ? AppBitmap.GetMeta(ix.Image) : string.Empty;
            sb.Append($" {meta}");

            pLabels[index].Text = sb.ToString();

            pLabels[index].Background = System.Windows.Media.Brushes.White;
            if (ix.Img.Family > 0) {
                var h = (ix.Img.Family - 1) * 360.0 / 16;
                const double s = 0.8, l = 0.8;
                var c = (1 - Math.Abs(2 * l - 1)) * s;
                var x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
                var m = l - c / 2;
                double r1, g1, b1;
                if      (h < 60)  { r1 = c; g1 = x; b1 = 0; }
                else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
                else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
                else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
                else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
                else              { r1 = c; g1 = 0; b1 = x; }
                var r = (byte)((r1 + m) * 255);
                var g = (byte)((g1 + m) * 255);
                var b = (byte)((b1 + m) * 255);
                pLabels[index].Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(r, g, b));
            }
        }

        RedrawCanvas();
    }

    private void RedrawCanvas()
    {
        var ws = new double[2];
        var hs = new double[2];
        for (var index = 0; index < 2; index++) {
            var panel = _images.GetPanel(index);
            if (!panel.HasValue) {
                return;
            }

            ws[index] = panel!.Value.DisplayWidth;
            hs[index] = panel.Value.DisplayHeight;
        }

        var aW = _picsMaxWidth / (ws[0] + ws[1]);
        var aH = _picsMaxHeight / Math.Max(hs[0], hs[1]);
        var a = Math.Min(aW, aH);
        if (a > 1.0) {
            a = 1.0;
        }

        SizeToContent = SizeToContent.Manual;
        Grid.ColumnDefinitions[0].Width = new GridLength(ws[0] * a, GridUnitType.Pixel);
        Grid.ColumnDefinitions[1].Width = new GridLength(ws[1] * a, GridUnitType.Pixel);
        Grid.RowDefinitions[0].Height = new GridLength(Math.Max(hs[0], hs[1]) * a, GridUnitType.Pixel);
        Grid.Width = (ws[0] + ws[1]) * a;
        Grid.Height = Math.Max(hs[0], hs[1]) * a + _labelMaxHeight;
        SizeToContent = SizeToContent.WidthAndHeight;
        Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - AppConsts.WindowMargin - Width) / 2;
        Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - AppConsts.WindowMargin - Height) / 2;
    }

    private async void ImgPanelExport()
    {
        DisableElements();
        await Task.Run(() => { _images.Export(_progress); }).ConfigureAwait(true);
        EnableElements();
    }

    private async void ImgPanelDeleteLeft()
    {
        DisableElements();
        string? closestHash = null;
        await Task.Run(() => { closestHash = _images.DeleteLeft(_progress); }).ConfigureAwait(true);
        await Task.Run(() => { _images.Find(closestHash, _progress); }).ConfigureAwait(true);
        DrawCanvas();
        EnableElements();
    }

    private async void ImgPanelDeleteRight()
    {
        DisableElements();
        string? closestHash = null;
        await Task.Run(() => { closestHash = _images.DeleteRight(_progress); }).ConfigureAwait(true);
        await Task.Run(() => { _images.Find(closestHash, _progress); }).ConfigureAwait(true);
        DrawCanvas();
        EnableElements();
    }

    private async void RotateClick(RotateMode rotatemode)
    {
        if (_images.GetPanel(0)?.VideoPath != null) return;
        DisableElements();
        var hash = _images.GetPanel(0)!.Value.Hash;
        var img = _images.GetPanel(0)!.Value.Img;
        await Task.Run(() => { _images.Rotate(hash, rotatemode, img.FlipMode); }).ConfigureAwait(true);
        await Task.Run(() => { _images.Find(hash, _progress); }).ConfigureAwait(true);
        DrawCanvas();
        EnableElements();
    }

    private async void FlipClick(FlipMode flipmode)
    {
        if (_images.GetPanel(0)?.VideoPath != null) return;
        DisableElements();
        var hash = _images.GetPanel(0)!.Value.Hash;
        var img = _images.GetPanel(0)!.Value.Img;
        await Task.Run(() => { _images.Rotate(hash, img.RotateMode, flipmode); }).ConfigureAwait(true);
        await Task.Run(() => { _images.Find(hash, _progress); }).ConfigureAwait(true);
        DrawCanvas();
        EnableElements();
    }

    private static void CloseApp()
    {
        System.Windows.Application.Current.Shutdown();
    }

    private void RefreshClick()
    {
        DisableElements();
        DrawCanvas();
        EnableElements();
    }

    private void ToggleXorClick()
    {
        DisableElements();
        _images.ShowXOR = !_images.ShowXOR;
        _images.UpdateRightPanel();
        DrawCanvas();
        EnableElements();
    }

    private void FamilyAddClick()
    {
        DisableElements();
        _images.FamilyAdd();
        DrawCanvas();
        EnableElements();
    }

    private void FamilyRemoveClick()
    {
        DisableElements();
        _images.FamilyRemove();
        DrawCanvas();
        EnableElements();
    }

    private void FamilySetClick(int id)
    {
        DisableElements();
        _images.FamilySet(id);
        DrawCanvas();
        EnableElements();
    }

    private void OnClosed()
    {
        VideoLeft.Stop();
        VideoRight.Stop();
        VideoLeft.Source = null;
        VideoRight.Source = null;
        AppVideoServer.Stop();
    }

    private void OnKeyDown(Key key)
    {
        switch (key) {
            case Key.V:
                ToggleXorClick();
                break;
            case Key.A:
                FamilyAddClick();
                break;
            case Key.D:
                FamilyRemoveClick();
                break;
        }
    }
}