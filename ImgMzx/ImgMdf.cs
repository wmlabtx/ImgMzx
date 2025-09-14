using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.IO;
using System.Security.Policy;

namespace ImgMzx
{
    public static partial class ImgMdf
    {
        public static void Find(string? hashX, IProgress<string>? progress)
        {
            Img? imgX = null;
            do {
                var totalcount = AppDatabase.Count();
                if (totalcount < 2) {
                    progress?.Report($"totalcount = {totalcount}");
                    return;
                }

                if (hashX != null) {
                    imgX = AppDatabase.GetImg(hashX);
                }

                if (imgX == null) {
                    hashX = AppDatabase.GetX(progress);
                    if (hashX == null) {
                        progress?.Report($"totalcount = {totalcount}");
                        return;
                    }
                }
                
                if (!AppPanels.SetLeftPanel(hashX!, progress)) {
                    Delete(hashX!);
                    hashX = null;
                    imgX = null;
                    continue;
                }

                var hashY = AppDatabase.GetY(hashX!, progress);
                if (!AppPanels.SetRightPanel(hashY!, progress)) {
                    Delete(hashY);
                    hashX = null;
                    imgX = null;
                    continue;
                }

                break;
            }
            while (true);
        }

        public static void Rotate(string hash, RotateMode rotatemode, FlipMode flipmode)
        {
            var filename = AppFile.GetFileName(hash, AppConsts.PathHp);
            var imagedata = AppFile.ReadEncryptedFile(filename);
            if (imagedata == null) {
                return;
            }

            using var image = AppBitmap.GetImage(imagedata, rotatemode, flipmode);
            if (image == null) {
                return;
            }

            var rvector = AppVit.GetVector(image);
            AppDatabase.ImgUpdateProperty(hash, AppConsts.AttributeVector, Helper.ArrayFromFloat(rvector));
            AppDatabase.ImgUpdateProperty(hash, AppConsts.AttributeRotateMode, (int)rotatemode);
            AppDatabase.ImgUpdateProperty(hash, AppConsts.AttributeFlipMode, (int)flipmode);
        }

        public static void Delete(string hashD)
        {
            var filename = AppFile.GetFileName(hashD, AppConsts.PathHp);
            DeleteEncryptedFile(filename);
            AppDatabase.Delete(hashD);
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

        public static string Export(string hashE)
        {
            var filename = AppFile.GetFileName(hashE, AppConsts.PathHp);
            var name = Path.GetFileNameWithoutExtension(filename).ToLower();
            var array = AppFile.ReadEncryptedFile(filename);
            if (array != null) {
                var ext = AppBitmap.GetExtension(array);
                var recycledName = AppFile.GetRecycledName(name, ext, AppConsts.PathGbProtected, DateTime.Now);
                AppFile.CreateDirectory(recycledName);
                File.WriteAllBytes(recycledName, array);
            }

            return name;
        }
    }
}