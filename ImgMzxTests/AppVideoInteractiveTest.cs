using FFMpegCore;
using ImgMzx;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ImgMzxTests;

[TestClass]
public class AppVideoInteractiveTest
{
    private static readonly string VideoPath =
        Path.Combine(AppContext.BaseDirectory, "videos", "harvest_luna.mp4");

    [TestMethod]
    public void PlayVideoFromBytes_Interactive()
    {
        Exception? caught = null;

        var thread = new Thread(() =>
        {
            try {
                GlobalFFOptions.Configure(o => o.BinaryFolder = AppContext.BaseDirectory);
                AppVideoServer.Start();

                var data = File.ReadAllBytes(VideoPath);
                var key = Guid.NewGuid().ToString("N");
                var url = AppVideoServer.RegisterTemp(key, data);

                var probe = FFProbe.Analyse(VideoPath);
                var v = probe.PrimaryVideoStream;

                var media = new MediaElement {
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Stop,
                    Stretch = Stretch.Uniform,
                    Source = new Uri(url)
                };
                media.MediaEnded += (_, _) => { media.Position = TimeSpan.Zero; media.Play(); };

                var btnPlay  = new Button { Content = "Play",  Width = 72, Margin = new Thickness(4, 0, 4, 0) };
                var btnPause = new Button { Content = "Pause", Width = 72, Margin = new Thickness(4, 0, 4, 0) };
                var btnStop  = new Button { Content = "Stop",  Width = 72, Margin = new Thickness(4, 0, 4, 0) };
                btnPlay .Click += (_, _) => media.Play();
                btnPause.Click += (_, _) => media.Pause();
                btnStop .Click += (_, _) => { media.Stop(); media.Position = TimeSpan.Zero; };

                var controls = new StackPanel {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 6)
                };
                controls.Children.Add(btnPlay);
                controls.Children.Add(btnPause);
                controls.Children.Add(btnStop);

                var info = new TextBlock {
                    Text = $"byte[] size: {data.Length / 1024} KB   →   served at {url}\n" +
                           $"{v?.Width}×{v?.Height}   {v?.CodecName}   {v?.FrameRate:F3} fps   " +
                           $"duration {probe.Duration:mm\\:ss\\.ff}",
                    TextAlignment = TextAlignment.Center,
                    FontFamily = new FontFamily("Consolas"),
                    Margin = new Thickness(4, 6, 4, 2)
                };

                var root = new DockPanel();
                DockPanel.SetDock(info, Dock.Top);
                DockPanel.SetDock(controls, Dock.Bottom);
                root.Children.Add(info);
                root.Children.Add(controls);
                root.Children.Add(media);

                var window = new Window {
                    Title = "Interactive Video Test – harvest_luna.mp4",
                    Content = root,
                    Width = 960,
                    Height = 620,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                window.Loaded += (_, _) => media.Play();
                window.Closed += (_, _) => {
                    media.Stop();
                    media.Source = null;
                    AppVideoServer.UnregisterTemp(key);
                    AppVideoServer.Stop();
                };

                window.ShowDialog();
            }
            catch (Exception ex) {
                caught = ex;
            }
         });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caught != null)
            throw caught;
    }
}
