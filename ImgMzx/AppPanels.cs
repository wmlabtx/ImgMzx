using System.Diagnostics;
using System.Text;
using SixLabors.ImageSharp.PixelFormats;

namespace ImgMzx
{
    public static class AppPanels
    {
        private static readonly ImgPanel[] _imgPanels = new ImgPanel[2];
        public static ImgPanel GetImgPanel(int idPanel)
        {
            return _imgPanels[idPanel];
        }

        private static List<string> _vector = new();
        private static int _position;

        private static bool SetPanel(
            string hash, 
            out byte[]? imagedata, 
            out Img? img, 
            out SixLabors.ImageSharp.Image<Rgb24>? image,
            out string extension, 
            out DateTime? taken,
            out int familysize)
        {
            imagedata = null;
            img = null;
            image = null;
            extension = "xxx";
            taken = null;
            familysize = 0;
            if (!AppImgs.TryGet(hash, out img)) {
                return false;
            }

            Debug.Assert(img != null);
            var filename = AppFile.GetFileName(img.Name, AppConsts.PathHp);
            Debug.Assert(filename != null);
            imagedata = AppFile.ReadEncryptedFile(filename);
            if (imagedata == null) {
                return false;
            }

            extension = AppBitmap.GetExtension(imagedata);
            image = AppBitmap.GetImage(imagedata, img.RotateMode, img.FlipMode);
            if (image == null) {
                return false;
            }

            taken = AppBitmap.GetDateTaken(image);
            return true;
        }

        private static void GetHorizon(int position, out string horizon, out int counter, out string nodes)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < position; i++) {
                sb.Append(_vector[position]);
            }

            horizon = position > 0 ? _vector[position - 1] : string.Empty;
            counter = position;
            nodes = sb.Length > 0 ? AppHash.GetHash(Encoding.ASCII.GetBytes(sb.ToString())) : string.Empty;
        }

        public static bool SetLeftPanel(string hash, IProgress<string>? progress)
        {
            progress?.Report($"Rendering left{AppConsts.CharEllipsis}");

            if (!SetPanel(hash,
                    out var imagedata,
                    out var img, 
                    out var image,
                    out var extension,
                    out var taken,
                    out var familysize)) {
                return false;
            }

            Debug.Assert(imagedata != null);
            Debug.Assert(img != null);
            Debug.Assert(image != null);

            progress?.Report($"Getting vector{AppConsts.CharEllipsis}");

            _vector = AppImgs.GetVector(img);
            _position = 0;
            if (img.Counter > 0) {
                while (_position < _vector.Count && string.Compare(_vector[_position], img.Horizon, StringComparison.Ordinal) <= 0) {
                    _position++;
                }

                GetHorizon(_position, out var horizon, out var counter, out var nodes);
                if (!nodes.Equals(img.Nodes)) {
                    img = AppImgs.SetHorizonCounterNodes(hash, horizon, counter, nodes);
                    if (img == null) {
                        return false;
                    }
                }
            }

            var imgpanel = new ImgPanel(
                img: img,
                size: imagedata.LongLength,
                image: image,
                extension: extension,
                taken: taken,
                familysize: familysize);
            _imgPanels[0] = imgpanel;
            return true;
        }

        public static bool UpdateRightPanel(IProgress<string>? progress)
        {
            return SetRightPanel(_vector[_position][4..], progress);
        }

        public static bool SetRightPanel(string hash, IProgress<string>? progress)
        {
            progress?.Report($"Rendering right{AppConsts.CharEllipsis}");

            if (!SetPanel(hash,
                    out var imagedata,
                    out var img,
                    out var image,
                    out var extension,
                    out var taken,
                    out var familysize)) {
                return false;
            }

            Debug.Assert(imagedata != null);
            Debug.Assert(img != null);
            Debug.Assert(image != null);

            if (AppVars.ShowXOR) {
                AppBitmap.Composite(_imgPanels[0].Image, image, out var imagexor);
                image.Dispose();
                image = imagexor;
            }

            var imgpanel = new ImgPanel(
                img: img,
                size: imagedata.LongLength,
                image: image,
                extension: extension,
                taken: taken,
                familysize: familysize);

            _imgPanels[1] = imgpanel;
            UpdateStatus(progress);
            return true;
        }

        public static string GetRight()
        {
            return _vector[_position][4..];
        }

        public static void MoveRight(IProgress<string>? progress)
        {
            if (_position + 1 < _vector.Count) {
                _position++;
            }

            SetRightPanel(_vector[_position][4..], progress);
        }

        public static void MoveLeft(IProgress<string>? progress)
        {
            if (_position - 1 >= 0) {
                _position--;
            }

            SetRightPanel(_vector[_position][4..], progress);
        }

        public static void MoveToTheFirst(IProgress<string>? progress)
        { 
            _position = 0;
            SetRightPanel(_vector[_position][4..], progress);
        }

        public static void MoveToTheLast(IProgress<string>? progress)
        {
            _position = _vector.Count - 1;
            SetRightPanel(_vector[_position][4..], progress);
        }

        public static void Confirm()
        {
            AppImgs.UpdateLastView(_imgPanels[1].Img.Hash);
            var imgX = AppImgs.UpdateLastView(_imgPanels[0].Img.Hash);
            Debug.Assert(imgX != null);
            GetHorizon(_position + 1, out var horizon, out var counter, out var nodes); 
            imgX = AppImgs.SetHorizonCounterNodes(imgX.Hash, horizon, counter, nodes);
            Debug.Assert(imgX != null);
            if (!imgX.Verified) {
                AppImgs.UpdateVerified(imgX.Hash);
            }
        }

        public static void DeleteLeft()
        {
            ImgMdf.Delete(_imgPanels[0].Img.Hash);
            AppImgs.UpdateLastView(_imgPanels[1].Img.Hash);
        }

        public static void DeleteRight(IProgress<string>? progress)
        {
            var imgX = AppImgs.UpdateLastView(_imgPanels[0].Img.Hash);
            Debug.Assert(imgX != null);
            _imgPanels[0].Img = imgX;
            ImgMdf.Delete(_imgPanels[1].Img.Hash);

            _vector.RemoveAt(_position);
            if (_position >= _vector.Count) {
                _position = _vector.Count - 1;
            }

            if (progress != null) {
                SetRightPanel(_vector[_position][4..], progress);
            }
        }

        private static void UpdateStatus(IProgress<string>? progress)
        {
            var imgX = _imgPanels[0].Img;
            var imgY = _imgPanels[1].Img;
            var vdistance = AppVit.GetDistance(imgX.Vector, imgY.Vector);
            var fdistance = AppFace.GetDistance(imgX.Faces, imgY.Faces);
            var postion = _position;
            var vectorsize = _vector.Count;
            if (progress != null) {
                progress.Report($"{postion}/{vectorsize} v{vdistance:F4} f{fdistance:F4}");
            }
        }
    }
}
