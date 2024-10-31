using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.IO;

namespace ImgMzx
{
    public static partial class ImgMdf
    {
        public static void Find(string? hashX, IProgress<string>? progress)
        {
            Img? imgX = null;
            do {
                var totalcount = AppImgs.Count();
                if (totalcount < 2) {
                    progress?.Report($"totalcount = {totalcount}");
                    return;
                }

                if (!string.IsNullOrEmpty(hashX) && !AppImgs.TryGet(hashX, out imgX)) {
                    hashX = null;
                    imgX = null;
                }

                if (imgX == null) {
                    imgX = AppImgs.GetForView();
                    hashX = imgX.Hash;
                }

                Debug.Assert(hashX != null);
                if (!AppPanels.SetLeftPanel(hashX, progress)) {
                    Delete(hashX);
                    hashX = null;
                    continue;
                }

                var hashY = AppPanels.GetRight();
                if (!AppPanels.SetRightPanel(hashY, progress)) {
                    Delete(hashY);
                    hashX = null;
                    continue;
                }

                break;
            }
            while (true);
        }

        public static void Rotate(string hash, RotateMode rotatemode, FlipMode flipmode)
        {
            if (!AppImgs.TryGet(hash, out var img)) {
                return;
            }

            Debug.Assert(img != null);

            var filename = AppFile.GetFileName(img.Name, AppConsts.PathHp);
            var imagedata = AppFile.ReadEncryptedFile(filename);
            if (imagedata == null) {
                return;
            }

            using var image = AppBitmap.GetImage(imagedata, rotatemode, flipmode);
            if (image == null) {
                return;
            }

            var rvector = AppVit.GetVector(image);
            if (rvector == null) {
                return;
            }

            var rfaces = AppFace.GetVector(image);
            AppImgs.SetVectorFacesOrientation(
                hash: hash,
                vector: rvector,
                faces: rfaces,
                rotatemode: rotatemode,
                flipmode: flipmode);
        }

        public static void Delete(string hashD)
        {
            if (!AppImgs.TryGet(hashD, out var imgX)) {
                return;
            }

            Debug.Assert(imgX != null);

            var filename = AppFile.GetFileName(imgX.Name, AppConsts.PathHp);
            DeleteEncryptedFile(filename);
            AppImgs.Remove(hashD);
            AppImgs.Delete(hashD);
        }

        private static void DeleteFile(string filename)
        {
            if (!File.Exists(filename)) {
                return;
            }

            File.SetAttributes(filename, FileAttributes.Normal);
            var name = Path.GetFileNameWithoutExtension(filename).ToLower();
            var ext = Path.GetExtension(filename);
            while (ext.StartsWith(".")) {
                ext = ext[1..];
            }

            var recycledName = AppFile.GetRecycledName(name, ext, AppConsts.PathGbProtected, DateTime.Now);
            AppFile.CreateDirectory(recycledName);
            File.Move(filename, recycledName);
        }

        private static void DeleteEncryptedFile(string filename)
        {
            if (!File.Exists(filename)) {
                return;
            }

            File.SetAttributes(filename, FileAttributes.Normal);
            var name = Path.GetFileNameWithoutExtension(filename).ToLower();
            var array = AppFile.ReadEncryptedFile(filename);
            if (array != null) {
                var ext = AppBitmap.GetExtension(array);
                var recycledName = AppFile.GetRecycledName(name, ext, AppConsts.PathGbProtected, DateTime.Now);
                AppFile.CreateDirectory(recycledName);
                File.WriteAllBytes(recycledName, array);
            }

            File.Delete(filename);
        }
    }
}