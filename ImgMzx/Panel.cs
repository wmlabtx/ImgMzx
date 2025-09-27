using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImgMzx;

public struct Panel
{
    public string Hash;
    public Img Img;
    public long Size;
    public Image<Rgb24> Image;
    public string Extension;
    public DateTime? Taken;
}
