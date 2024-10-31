using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImgMzx
{
    public static class AppBitmap
    {
        public static string GetExtension(byte[] data)
        {
            string ext;
            try {
                var format = SixLabors.ImageSharp.Image.DetectFormat(data);
                ext = format.FileExtensions.First();
            }
            catch (UnknownImageFormatException) {
                ext = "xxx";
            }

            return ext;
        }

        public static Image<Rgb24>? GetImage(byte[] data)
        {
            try {
                var img = SixLabors.ImageSharp.Image.Load<Rgb24>(data);
                return img;
            }
            catch (UnknownImageFormatException) {
            }
            catch (InvalidImageContentException) {
            }

            return null;
        }

        public static Image<Rgb24>? GetImage(byte[] data, RotateMode rotatemode, FlipMode flipmode)
        {
            try {
                var img = SixLabors.ImageSharp.Image.Load<Rgb24>(data);
                if (rotatemode == RotateMode.None && flipmode == FlipMode.None) {
                    return img;
                }

                var imgtemp = img.Clone(e => e
                    .Flip(flipmode)
                    .Rotate(rotatemode)
                );

                img.Dispose();
                return imgtemp;
            }
            catch (UnknownImageFormatException) {
            }

            return null;
        }

        public static Bitmap GetBitmap(Image<Rgb24> image)
        {
            using var ms = new MemoryStream();
            image.Save(ms, new BmpEncoder());
            ms.Position = 0;
            using var tempBitmap = new Bitmap(ms);
            return new Bitmap(tempBitmap);
        }

        public static ImageSource GetImageSource(Image<Rgb24> image)
        {
            var ms = new MemoryStream();
            image.Save(ms, new BmpEncoder());
            ms.Position = 0;
            var imageSource = new BitmapImage();
            imageSource.BeginInit();
            imageSource.StreamSource = ms;
            imageSource.EndInit();
            return imageSource;
        }

        public static DateTime? GetDateTaken(Image<Rgb24> image)
        {
            DateTime? dateTaken = null;

            if (image.Metadata.IptcProfile != null) {
                var profile = image.Metadata.IptcProfile;
                var list = profile.GetValues(IptcTag.CreatedDate);
                string? dateCreatedValue = null;
                if (list.Count > 0) {
                    dateCreatedValue = list[0].Value;
                }

                list = profile.GetValues(IptcTag.CreatedTime);
                string? timeCreatedValue = null;
                if (list.Count > 0) {
                    timeCreatedValue = list[0].Value;
                }

                if (dateCreatedValue != null && timeCreatedValue != null) {
                    if (DateTime.TryParseExact(
                            dateCreatedValue,
                            "yyyyMMdd", 
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None,
                            out DateTime datePart) &&
                        DateTime.TryParseExact(
                            timeCreatedValue,
                            "HHmmsszzz",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None,
                            out DateTime timePart)) {
                        dateTaken = new DateTime(
                            datePart.Year,
                            datePart.Month,
                            datePart.Day,
                            timePart.Hour,
                            timePart.Minute,
                            timePart.Second);
                    }
                }
                else if (dateCreatedValue != null) {
                    if (DateTime.TryParseExact(
                            dateCreatedValue,
                            "yyyyMMdd",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None,
                            out DateTime dateOnly)) {
                        dateTaken = dateOnly;
                    }
                }
            }

            if (image.Metadata.ExifProfile != null) {
                var profile = image.Metadata.ExifProfile;
                if (profile.TryGetValue(ExifTag.DateTimeDigitized, out var dateTimeValue)) {
                    if (DateTime.TryParseExact(
                            dateTimeValue.Value,
                            "yyyy:MM:dd HH:mm:ss",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None,
                            out DateTime dt)) {
                        if (dateTaken == null || dt < dateTaken) {
                            dateTaken = dt;
                        }
                    }
                }

                if (profile.TryGetValue(ExifTag.DateTimeOriginal, out dateTimeValue)) {
                    if (DateTime.TryParseExact(
                            dateTimeValue.Value,
                            "yyyy:MM:dd HH:mm:ss",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None,
                            out DateTime dt)) {
                        if (dateTaken == null || dt < dateTaken) {
                            dateTaken = dt;
                        }
                    }
                }

                if (profile.TryGetValue(ExifTag.DateTime, out dateTimeValue)) {
                    if (DateTime.TryParseExact(
                            dateTimeValue.Value,
                            "yyyy:MM:dd HH:mm:ss",
                             System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None,
                            out DateTime dt)) {
                        if (dateTaken == null || dt < dateTaken) {
                            dateTaken = dt;
                        }
                    }
                }
            }

            if (image.Metadata.XmpProfile != null) {
                var profile = image.Metadata.XmpProfile;
                try {
                    var xmlDocument = profile.GetDocument();
                    if (xmlDocument != null) {
                        var raw = xmlDocument.ToString();
                        var regex = new Regex(@"\b\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:Z|[+\-]\d{2}:\d{2})\b",
                            RegexOptions.Compiled);
                        var matches = regex.Matches(raw);
                        foreach (var match in matches) {
                            string? dateTimeString = match.ToString();
                            if (DateTimeOffset.TryParseExact(
                                    dateTimeString,
                                    "yyyy-MM-ddTHH:mm:ssK",
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.None,
                                    out DateTimeOffset dto)) {
                                if (dateTaken == null || dto.DateTime < dateTaken) {
                                    dateTaken = dto.DateTime;
                                }
                            }
                        }
                    }
                }
                catch (XmlException) {
                }
            }

            return dateTaken;
        }

        public static void Composite(Image<Rgb24> x, Image<Rgb24> y, out Image<Rgb24> zb)
        {
            var originalWidth = y.Width;
            var originalHeight = y.Height;

            var xb = x.Clone(ctx => ctx
                .Resize(new ResizeOptions {
                    Size = new SixLabors.ImageSharp.Size(512, 512),
                    Mode = ResizeMode.Stretch
                }));

            var yb = y.Clone(ctx => ctx
                .Resize(new ResizeOptions {
                    Size = new SixLabors.ImageSharp.Size(512, 512),
                    Mode = ResizeMode.Stretch
                }));

            zb = xb.CloneAs<Rgb24>();
            var go = new GraphicsOptions {
                ColorBlendingMode = PixelColorBlendingMode.Subtract,
                BlendPercentage = 0.9f
            };
            zb.Mutate(e => e.DrawImage(yb, go));
            zb.Mutate(e => e
                .Resize(new ResizeOptions {
                    Size = new SixLabors.ImageSharp.Size(originalWidth, originalHeight),
                    Mode = ResizeMode.Stretch
                }));

            yb.Dispose();
            xb.Dispose();
        }
    }
}
