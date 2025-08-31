using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using SixLabors.ImageSharp.Processing;

namespace ImgMzx;

public sealed partial class MainWindow
{
    private double _picsMaxWidth;
    private double _picsMaxHeight;
    private double _labelMaxHeight;

    private readonly NotifyIcon _notifyIcon = new();
    private BackgroundWorker _backgroundWorker = new() { WorkerSupportsCancellation = true, WorkerReportsProgress = true };

    private async void WindowLoaded()
    {
        BoxLeft.MouseDown += PictureLeftBoxMouseClick;
        BoxRight.MouseDown += PictureRightBoxMouseClick;

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

        AppVars.Progress = new Progress<string>(message => Status.Text = message);

        await Task.Run(() => { AppImgs.Load(AppConsts.FileDatabase, AppVars.Progress, out int maxImages, out int lcId, out int lvId); 
            AppVars.MaxImages = maxImages; 
            AppVars.LcId = lcId;
            AppVars.LvId = lvId;    
        }).ConfigureAwait(true);
        await Task.Run(() => { ImgMdf.Find(null, AppVars.Progress); }).ConfigureAwait(true);

        /*
        if (AppImgs.TryGetByName("580b77", out var img1) && AppImgs.TryGetByName("86f22e", out var img2)) {
            var filename0 = ImgMdf.Export(img1!.Hash);
            var filename1 = ImgMdf.Export(img2!.Hash);
        }
        */

        DrawCanvas();

        AppVars.SuspendEvent = new ManualResetEvent(true);

        _backgroundWorker = new BackgroundWorker { WorkerSupportsCancellation = true, WorkerReportsProgress = true };
        _backgroundWorker.DoWork += DoCompute;
        _backgroundWorker.ProgressChanged += DoComputeProgress;
        _backgroundWorker.RunWorkerAsync();
    }

    private void OnStateChanged()
    {
        if (WindowState != WindowState.Minimized) {
            return;
        }

        Hide();
        _notifyIcon.Visible = true;
    }

    private static void ImportClick()
    {
        AppVars.ImportRequested = true;
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
        await Task.Run(() => { AppPanels.Confirm(AppVars.Progress); }).ConfigureAwait(true);
        await Task.Run(() => { ImgMdf.Find(null, AppVars.Progress); }).ConfigureAwait(true);
        DrawCanvas();
        EnableElements();
    }

    private async void ButtonRightNextMouseClick()
    {
        DisableElements();
        await Task.Run(() => { AppPanels.Confirm(AppVars.Progress); }).ConfigureAwait(true);
        var hashX = AppPanels.GetImgPanel(0)!.Img.Hash;
        await Task.Run(() => { ImgMdf.Find(hashX, AppVars.Progress); }).ConfigureAwait(true);
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
        LabelLeft.IsEnabled = enabled;
        LabelRight.IsEnabled = enabled;
    }

    private void DrawCanvas()
    {
        var panels = new ImgPanel?[2];
        panels[0] = AppPanels.GetImgPanel(0);
        panels[1] = AppPanels.GetImgPanel(1);
        if (panels[0] == null || panels[1] == null) {
            return;
        }

        var pBoxes = new[] { BoxLeft, BoxRight };
        var pLabels = new[] { LabelLeft, LabelRight };

        for (var index = 0; index < 2; index++) {
            pBoxes[index].Source = AppBitmap.GetImageSource(panels[index]!.Image);
            var sb = new StringBuilder();
            sb.Append($"{panels[index]!.Img.Name}.{panels[index]!.Extension}");

            if (panels[index]!.Img.Score > 0) {
                sb.Append($" +{panels[index]!.Img.Score}");
            }

            if (panels[index]!.Img.Id > 0) {
                sb.Append($" #{panels[index]!.Img.Id:D3}");
            }

            sb.AppendLine();

            sb.Append($"{Helper.SizeToString(panels[index]!.Size)} ");
            sb.Append($" ({panels[index]!.Image.Width}x{panels[index]!.Image.Height})");
            sb.AppendLine();

            sb.Append($" {Helper.TimeIntervalToString(DateTime.Now.Subtract(panels[index]!.Img.LastView))} ago ");
            var dateTime = panels[index]!.Taken;
            if (dateTime != null) {
                sb.Append($" [{dateTime.Value.ToShortDateString()}]");
            }

            var meta = AppBitmap.GetMeta(panels[index]!.Image);
            sb.Append($" {meta}");

            pLabels[index].Text = sb.ToString();
            pLabels[index].Background = System.Windows.Media.Brushes.White;
            if (panels[index]!.Img.Score == 0) {
                pLabels[index].Background = System.Windows.Media.Brushes.Yellow;
            }
        }

        RedrawCanvas();
    }

    private void RedrawCanvas()
    {
        var ws = new double[2];
        var hs = new double[2];
        for (var index = 0; index < 2; index++) {
            var panel = AppPanels.GetImgPanel(index);
            ws[index] = panel!.Image.Width;
            hs[index] = panel.Image.Height;
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
        await Task.Run(() => { AppPanels.Export(AppVars.Progress); }).ConfigureAwait(true);
        EnableElements();
    }

    private async void ImgPanelDeleteLeft()
    {
        DisableElements();
        await Task.Run(() => { AppPanels.DeleteLeft(AppVars.Progress); }).ConfigureAwait(true);
        await Task.Run(() => { ImgMdf.Find(null, AppVars.Progress); }).ConfigureAwait(true);
        DrawCanvas();
        EnableElements();
    }

    private async void ImgPanelDeleteRight()
    {
        DisableElements();
        await Task.Run(() => { AppPanels.DeleteRight(AppVars.Progress); }).ConfigureAwait(true);
        await Task.Run(() => { ImgMdf.Find(null, AppVars.Progress); }).ConfigureAwait(true);
        DrawCanvas();
        EnableElements();
    }

    private async void RotateClick(RotateMode rotatemode)
    {
        DisableElements();
        var img = AppPanels.GetImgPanel(0)!.Img;
        await Task.Run(() => { ImgMdf.Rotate(img.Hash, rotatemode, img.FlipMode); }).ConfigureAwait(true);
        await Task.Run(() => { ImgMdf.Find(img.Hash, AppVars.Progress); }).ConfigureAwait(true);
        DrawCanvas();
        EnableElements();
    }

    private async void FlipClick(FlipMode flipmode)
    {
        DisableElements();
        var img = AppPanels.GetImgPanel(0)!.Img;
        await Task.Run(() => { ImgMdf.Rotate(img.Hash, img.RotateMode, flipmode); }).ConfigureAwait(true);
        await Task.Run(() => { ImgMdf.Find(img.Hash, AppVars.Progress); }).ConfigureAwait(true);
        DrawCanvas();
        EnableElements();
    }

    private void ReleaseResources()
    {
        _notifyIcon.Dispose();
        _backgroundWorker.CancelAsync();
        _backgroundWorker.Dispose();
    }

    private void CloseApp()
    {
        ReleaseResources();
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
        AppVars.ShowXOR = !AppVars.ShowXOR;
        AppPanels.UpdateRightPanel(AppVars.Progress);
        DrawCanvas();
        EnableElements();
    }

    private void FamilySetClick(string family)
    {
        /*
        DisableElements();
        var imgX = AppPanels.GetImgPanel(0)!.Img;
        imgX.SetFamily(family);
        DrawCanvas();
        EnableElements();
        */
    }

    private void FamilyAddClick()
    {
        /*
        DisableElements();
        var imgX = AppPanels.GetImgPanel(0)!.Img;
        var imgY = AppPanels.GetImgPanel(1)!.Img;
        if (string.IsNullOrEmpty(imgX.Family) && !string.IsNullOrEmpty(imgY.Family)) {
            imgX.SetFamily(imgY.Family);
        }
        else if (!string.IsNullOrEmpty(imgX.Family) && string.IsNullOrEmpty(imgY.Family)) {
            imgY.SetFamily(imgX.Family);
        }

        DrawCanvas();
        EnableElements();
        */
    }

    private void FamilyRemoveClick()
    {
        /*
        DisableElements();
        var imgX = AppPanels.GetImgPanel(0)!.Img;
        var imgY = AppPanels.GetImgPanel(1)!.Img;
        if (imgX.Family != imgY.Family) {
            imgX.SetFamily(0);
            imgY.SetFamily(0);
        }

        DrawCanvas();
        EnableElements();
        */
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

    private void DoComputeProgress(object? sender, ProgressChangedEventArgs e)
    {
        BackgroundStatus.Text = e.UserState != null ? (string)e.UserState : string.Empty;
    }

    private void DoCompute(object? sender, DoWorkEventArgs args)
    {
        while (!_backgroundWorker.CancellationPending) {
            ImgMdf.BackgroundWorker(_backgroundWorker);
        }

        args.Cancel = true;
    }
}