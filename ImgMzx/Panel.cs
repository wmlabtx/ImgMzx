using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Windows.Media;

namespace ImgMzx;

public struct Panel
{
    public string Hash;
    public Img Img;
    public long Size;
    public Image<Rgb24> Image;
    public string Extension;
    public DateTime? Taken;
    public ImageSource[]? AnimatedFrames;
    public int[]? FrameDelaysMs;
}
