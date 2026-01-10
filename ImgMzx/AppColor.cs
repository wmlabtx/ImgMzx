namespace ImgMzx;

public static class AppColor
{
    public static (float L, float a, float b) RgbToOklab(byte rb, byte gb, byte bb)
    {
        // Convert sRGB to linear RGB
        float linR = SrgbToLinear(rb / 255f);
        float linG = SrgbToLinear(gb / 255f);
        float linB = SrgbToLinear(bb / 255f);

        // Convert linear RGB to LMS
        float lmsL = 0.4122214708f * linR + 0.5363325363f * linG + 0.0514459929f * linB;
        float lmsM = 0.2119034982f * linR + 0.6806995451f * linG + 0.1073969566f * linB;
        float lmsS = 0.0883024619f * linR + 0.2817188376f * linG + 0.6299787005f * linB;

        // Apply cube root
        float lmsL_ = MathF.Cbrt(lmsL);
        float lmsM_ = MathF.Cbrt(lmsM);
        float lmsS_ = MathF.Cbrt(lmsS);

        // Convert to Oklab
        float l = 0.2104542553f * lmsL_ + 0.7936177850f * lmsM_ - 0.0040720468f * lmsS_;
        float a = 1.9779984951f * lmsL_ - 2.4285922050f * lmsM_ + 0.4505937099f * lmsS_;
        float b = 0.0259040371f * lmsL_ + 0.7827717662f * lmsM_ - 0.8086757660f * lmsS_;

        return (l, a, b);
    }

    private static float SrgbToLinear(float c)
    {
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }
}
