using SixLabors.ImageSharp.Processing;

namespace ImgMzx;

public struct Img
{
    public string Hash;
    public DateTime LastView;
    public DateTime LastCheck;
    public RotateMode RotateMode;
    public FlipMode FlipMode;
    public int Score;
    public string Next;
    public float Distance;
}