﻿using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImgMzx
{
    public class ImgPanel
    {
        public Img Img { get; set; }
        public long Size { get; }
        public Image<Rgb24> Image { get; private set; }
        public string Extension { get; }
        public DateTime? Taken { get; }

        public ImgPanel(Img img, long size, Image<Rgb24> image, string extension, DateTime? taken)
        {
            Img = img;
            Size = size;
            Image = image;
            Extension = extension;
            Taken = taken;
        }
    }
}
