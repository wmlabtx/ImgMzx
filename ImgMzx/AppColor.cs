using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.PixelFormats;

namespace ImgMzx;

public static class AppColor
{
    private const float RefWhiteX = 0.95047f;
    private const float RefWhiteY = 1.00000f;
    private const float RefWhiteZ = 1.08883f;

    private const float Epsilon = 0.008856f; // (6/29)^3
    private const float Kappa = 903.3f;       // (29/3)^3

    private static readonly float[] SrgbToLinear = BuildSrgbToLinearLut();

    private static float[] BuildSrgbToLinearLut()
    {
        var lut = new float[256];
        for (var i = 0; i < 256; i++) {
            var c = i / 255.0f;
            lut[i] = c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        return lut;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (float L, float A, float B) RgbToLab(byte r, byte g, byte b)
    {
        var rl = SrgbToLinear[r];
        var gl = SrgbToLinear[g];
        var bl = SrgbToLinear[b];

        var x = (0.4124564f * rl + 0.3575761f * gl + 0.1804375f * bl) / RefWhiteX;
        var y = (0.2126729f * rl + 0.7151522f * gl + 0.0721750f * bl) / RefWhiteY;
        var z = (0.0193339f * rl + 0.1191920f * gl + 0.9503041f * bl) / RefWhiteZ;

        var fx = LabF(x);
        var fy = LabF(y);
        var fz = LabF(z);

        var l = 116.0f * fy - 16.0f;
        var a = 500.0f * (fx - fy);
        var b2 = 200.0f * (fy - fz);

        return (l, a, b2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (float L, float A, float B) RgbToLab(Rgb24 pixel) => RgbToLab(pixel.R, pixel.G, pixel.B);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float LabF(float t)
    {
        return t > Epsilon ? MathF.Cbrt(t) : (Kappa * t + 16.0f) / 116.0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(float l1, float a1, float b1, float l2, float a2, float b2)
    {
        var dl = l1 - l2;
        var da = a1 - a2;
        var db = b1 - b2;
        return MathF.Sqrt(dl * dl + da * da + db * db);
    }
}
