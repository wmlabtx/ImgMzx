using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using Image = System.Drawing.Image;
using Rectangle = System.Drawing.Rectangle;

namespace ImgMzx;

public static class AppVit
{
    private static readonly InferenceSession _session = new(AppConsts.FileVit);

    private static Bitmap ScaleAndCut(Image bitmap, int dim, int border)
    {
        var bigdim = dim + (border * 2);
        int width;
        int height;
        if (bitmap.Width >= bitmap.Height) {
            height = bigdim;
            width = (int)Math.Round(bitmap.Width * bigdim / (float)bitmap.Height);
        }
        else {
            width = bigdim;
            height = (int)Math.Round(bitmap.Height * bigdim / (float)bitmap.Width);
        }

        using var bitmapbigdim = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bitmapbigdim)) {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(bitmap, 0, 0, width, height);
        }

        int x;
        int y;
        if (width >= height) {
            x = border + (width - height) / 2;
            y = border;
        }
        else {
            x = border;
            y = border + (height - width) / 2;
        }

        var bitmapdim = bitmapbigdim.Clone(new Rectangle(x, y, dim, dim), PixelFormat.Format24bppRgb);
        return bitmapdim;
    }


    public static float[] GetVector(Image<Rgb24> image)
    {
        using var bitmap = AppBitmap.GetBitmap(image);
        var tensor = new DenseTensor<float>(new[] { 1, 3, 224, 224 });
        using (var b = ScaleAndCut(bitmap, 224, 16)) {
            var bitmapdata = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);
            var stride = bitmapdata.Stride;
            var data = new byte[Math.Abs(bitmapdata.Stride * bitmapdata.Height)];
            Marshal.Copy(bitmapdata.Scan0, data, 0, data.Length);
            b.UnlockBits(bitmapdata);
            var width = b.Width;
            Parallel.For(0, b.Height, y => {
                var offsetx = y * stride;
                for (var x = 0; x < width; x++) {
                    var rbyte = data[offsetx + 2];
                    var gbyte = data[offsetx + 1];
                    var bbyte = data[offsetx];
                    offsetx += 3;

                    var red = (rbyte / 255f - 0.48145466f) / 0.26862954f;
                    var green = (gbyte / 255f - 0.48145466f) / 0.26862954f;
                    var blue = (bbyte / 255f - 0.40821073f) / 0.27577711f;
                    tensor[0, 0, y, x] = red;
                    tensor[0, 1, y, x] = green;
                    tensor[0, 2, y, x] = blue;
                }
            });
        }

        var container = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input", tensor) };
        var results = _session.Run(container);
        var vector = results[0].AsEnumerable<float>().ToArray();
        var norm = (float)Math.Sqrt(vector.Sum(t => t * t));
        Parallel.For(0, vector.Length, i => {
            vector[i] /= norm;
        });

        return vector;
    }

    public static float GetDistance(float[] x, float[] y)
    {
        if (x.Length == 0 || y.Length == 0 || x.Length != y.Length) {
            return 1.1f;
        }

        var distance = x.Select((t, i) => t * y[i]).Sum();
        distance = 1f - distance;
        return distance;
    }

    public static float GetDeviation(float[] vector)
    {
        var mean = vector.Sum() / vector.Length;
        var deviation = vector.Sum(t => Math.Abs(t - mean)) / vector.Length;
        return deviation;
    }
}