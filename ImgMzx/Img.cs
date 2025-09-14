using SixLabors.ImageSharp.Processing;

namespace ImgMzx;

public struct Img
{
    public DateTime LastView;
    public DateTime LastCheck;
    public RotateMode RotateMode;
    public FlipMode FlipMode;
    public bool Verified;
    public int Score;
    public string Next;
    public float Distance;
}