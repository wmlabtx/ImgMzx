using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;

namespace ImgMzx; 

public static class AppBitmap
{
    private static readonly ConcurrentDictionary<string, string> _formatCache = new();
    private static readonly Regex _xmpDateRegex = new(
        @"\b\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:Z|[+\-]\d{2}:\d{2})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static string GetExtension(ReadOnlySpan<byte> data)
    {
        try
        {
            var signature = GetFileSignature(data);
            if (_formatCache.TryGetValue(signature, out var cachedExt))
            {
                return cachedExt;
            }

            var format = SixLabors.ImageSharp.Image.DetectFormat(data);
            var extension = format.FileExtensions.First();
                
            _formatCache.TryAdd(signature, extension);
                
            return extension;
        }
        catch (UnknownImageFormatException)
        {
            return "xxx";
        }
    }

    public static string GetExtension(byte[] data) => GetExtension(data.AsSpan());

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static Image<Rgb24>? GetImage(ReadOnlySpan<byte> data, RotateMode rotatemode = RotateMode.None, FlipMode flipmode = FlipMode.None)
    {
        try {
            var img = SixLabors.ImageSharp.Image.Load<Rgb24>(data);
                
            if (rotatemode == RotateMode.None && flipmode == FlipMode.None) {
                return img;
            }

            if (CanTransformInPlace(rotatemode, flipmode)) {
                img.Mutate(e => e.Flip(flipmode).Rotate(rotatemode));
                return img;
            }

            var transformed = img.Clone(e => e.Flip(flipmode).Rotate(rotatemode));
            img.Dispose();
            return transformed;
        }
        catch {
            return null;
        }
    }

    public static Image<Rgb24>? GetImage(byte[] data, RotateMode rotatemode = RotateMode.None, FlipMode flipmode = FlipMode.None)
        => GetImage(data.AsSpan(), rotatemode, flipmode);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static ImageSource GetImageSource(Image<Rgb24> image)
    {
        using var ms = new MemoryStream();
        image.Save(ms, new BmpEncoder());

        var imageData = ms.ToArray();
        var streamForBitmap = new MemoryStream(imageData);

        var imageSource = new BitmapImage();
        imageSource.BeginInit();
        imageSource.CacheOption = BitmapCacheOption.OnLoad; // Loads data immediately
        imageSource.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        imageSource.StreamSource = streamForBitmap;
        imageSource.EndInit();
        imageSource.Freeze();

        streamForBitmap.Dispose();

        return imageSource;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static DateTime? GetDateTaken(Image<Rgb24> image)
    {
        DateTime? dateTaken = null;

        if (image.Metadata.IptcProfile?.Values != null)
        {
            dateTaken = ExtractIptcDate(image.Metadata.IptcProfile);
        }

        if (image.Metadata.ExifProfile != null)
        {
            var exifDate = ExtractExifDate(image.Metadata.ExifProfile);
            if (exifDate.HasValue && (dateTaken == null || exifDate < dateTaken))
            {
                dateTaken = exifDate;
            }
        }

        if (image.Metadata.XmpProfile != null)
        {
            var xmpDate = ExtractXmpDate(image.Metadata.XmpProfile);
            if (xmpDate.HasValue && (dateTaken == null || xmpDate < dateTaken))
            {
                dateTaken = xmpDate;
            }
        }

        return dateTaken;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static string GetMeta(Image<Rgb24> image)
    {
        var sb = new System.Text.StringBuilder(50);
        var metadata = image.Metadata;
            
        if (metadata.ExifProfile?.Values != null) {
            sb.Append($"E:{metadata.ExifProfile.Values.Count} ");
        }

        if (metadata.IptcProfile?.Values != null) {
            sb.Append($"P:{metadata.IptcProfile.Values.Count()} ");
        }

        if (metadata.IccProfile?.Entries != null) {
            sb.Append($"C:{metadata.IccProfile.Entries.Length} ");
        }

        if (metadata.XmpProfile != null) {
            sb.Append($"X:{metadata.XmpProfile.ToByteArray().Length}");
        }

        var result = sb.ToString().TrimEnd();
        return result.Length > 30 ? result[..30] : result;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void Composite(Image<Rgb24> ix, Image<Rgb24> iy, out Image<Rgb24> zb)
    {
        var originalWidth = iy.Width;
        var originalHeight = iy.Height;
        const int targetSize = 512;

        using var xb = ix.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new SixLabors.ImageSharp.Size(targetSize, targetSize),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Lanczos3
        }));

        using var yb = iy.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new SixLabors.ImageSharp.Size(targetSize, targetSize),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Lanczos3
        }));

        zb = new Image<Rgb24>(targetSize, targetSize);

        ProcessPixelsDifference(xb, yb, zb);

        zb.Mutate(e => e.Resize(new ResizeOptions
        {
            Size = new SixLabors.ImageSharp.Size(originalWidth, originalHeight),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Lanczos3
        }));
    }

    public static void ClearCache()
    {
        _formatCache.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetFileSignature(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8) return "unknown";
        return Convert.ToHexString(data[..8]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CanTransformInPlace(RotateMode rotatemode, FlipMode flipmode)
    {
        return rotatemode == RotateMode.Rotate180 || flipmode != FlipMode.None;
    }

    private static DateTime? ExtractIptcDate(IptcProfile profile)
    {
        try {
            var dateValues = profile.GetValues(IptcTag.CreatedDate);
            var timeValues = profile.GetValues(IptcTag.CreatedTime);

            string? dateCreated = dateValues.Count > 0 ? dateValues[0].Value : null;
            string? timeCreated = timeValues.Count > 0 ? timeValues[0].Value : null;

            if (dateCreated != null && timeCreated != null) {
                if (DateTime.TryParseExact(dateCreated, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var datePart) &&
                    DateTime.TryParseExact(timeCreated, "HHmmsszzz", null, System.Globalization.DateTimeStyles.None, out var timePart)) {
                    return new DateTime(datePart.Year, datePart.Month, datePart.Day, timePart.Hour, timePart.Minute, timePart.Second);
                }
            }
            else if (dateCreated != null) {
                if (DateTime.TryParseExact(dateCreated, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var dateOnly)) {
                    return dateOnly;
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static DateTime? ExtractExifDate(ExifProfile profile)
    {
        try {
            var tagsToCheck = new[] { ExifTag.DateTimeDigitized, ExifTag.DateTimeOriginal, ExifTag.DateTime };
            foreach (var tag in tagsToCheck) {
                if (profile.TryGetValue(tag, out var dateTimeValue) && dateTimeValue.Value != null) {
                    if (DateTime.TryParseExact(dateTimeValue.Value, "yyyy:MM:dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out var dt)) {
                        return dt;
                    }
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static DateTime? ExtractXmpDate(SixLabors.ImageSharp.Metadata.Profiles.Xmp.XmpProfile profile)
    {
        try {
            var xmlDocument = profile.GetDocument();
            if (xmlDocument != null) {
                var raw = xmlDocument.ToString();
                var matches = _xmpDateRegex.Matches(raw);
                foreach (Match match in matches) {
                    if (DateTimeOffset.TryParseExact(match.Value, "yyyy-MM-ddTHH:mm:ssK", null, System.Globalization.DateTimeStyles.None, out var dto)) {
                        return dto.DateTime;
                    }
                }
            }
        }
        catch
        {
        }
        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ProcessPixelsDifference(Image<Rgb24> xb, Image<Rgb24> yb, Image<Rgb24> zb)
    {
        const int targetSize = 512;
        const double threshold = 50.0;
        const float dimFactor = 0.2f;

        Parallel.For(0, targetSize, y =>
        {
            for (int x = 0; x < targetSize; x++) {
                var pixel1 = xb[x, y];
                var pixel2 = yb[x, y];

                var rDiff = pixel1.R - pixel2.R;
                var gDiff = pixel1.G - pixel2.G;
                var bDiff = pixel1.B - pixel2.B;

                var distance = Math.Sqrt(rDiff * rDiff + gDiff * gDiff + bDiff * bDiff);

                if (distance >= threshold) {
                    zb[x, y] = new Rgb24(255, 255, 255);
                }
                else {
                    var newR = (byte)(pixel1.R * dimFactor);
                    var newG = (byte)(pixel1.G * dimFactor);
                    var newB = (byte)(pixel1.B * dimFactor);
                    zb[x, y] = new Rgb24(newR, newG, newB);
                }
            }
        });
    }
}
