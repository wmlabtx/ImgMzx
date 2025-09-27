using SixLabors.ImageSharp.Processing;
using System.IO;
using System.Text;

namespace ImgMzx
{
    /*
    public static partial class ImgMdf
    {
        private static int _added;
        private static int _bad;
        private static int _found;
        private static DateTime _lastview = DateTime.Now;

        public static void Import(IProgress<string> progress)
        {
            AppVars.MaxImages = AppVars.MaxImages - 100;
            AppVars.Data.UpdateMaxImages(AppVars.MaxImages);
            _lastview = AppVars.Data.GetMinimalLastView().AddMinutes(-1);
            _added = 0;
            _found = 0;
            _bad = 0;
            ImportFiles(AppConsts.PathRawProtected, SearchOption.TopDirectoryOnly, progress);
            if (_added < AppConsts.MaxImportFiles) {
                var directoryInfo = new DirectoryInfo(AppConsts.PathRawProtected);
                var ds = directoryInfo.GetDirectories("*.*", SearchOption.TopDirectoryOnly).ToArray();
                foreach (var di in ds) {
                    ImportFiles(di.FullName, SearchOption.AllDirectories, progress);
                    if (_added >= AppConsts.MaxImportFiles) {
                        break;
                    }
                }
            }

            Helper.CleanupDirectories(AppConsts.PathRawProtected, progress);
            progress.Report($"Imported a:{_added}/f:{_found}/b:{_bad}");
        }

        private static void ImportFiles(string path, SearchOption so, IProgress<string> progress)
        {
            var directoryInfo = new DirectoryInfo(path);
            var fs = directoryInfo.GetFiles("*.*", so).ToArray();
            foreach (var e in fs) {
                var orgfilename = e.FullName;
                if (!ImportFile(orgfilename, progress)) {
                    break;
                }

                if (_added >= AppConsts.MaxImportFiles) {
                    break;
                }
            }

            progress.Report($"clean-up {path}{AppConsts.CharEllipsis}");
            Helper.CleanupDirectories(path, progress);
        }

        private static bool ImportFile(string orgfilename, IProgress<string> progress)
        {
            var orgname = Path.GetFileNameWithoutExtension(orgfilename);
            var hashByName = orgname.ToLowerInvariant();
            if (AppHash.IsValidHash(hashByName) && AppVars.Data.ContainsImg(hashByName)) {
                var imagedata = AppFile.ReadMex(hashByName);
                if (imagedata == null) {
                    var orgimagedata = AppFile.ReadFile(orgfilename);
                    if (orgimagedata == null) {
                        AppFile.MoveToRecycleBin(orgfilename);
                        _bad++;
                    }
                    else {
                        AppFile.WriteMex(hashByName, orgimagedata);
                        AppFile.MoveToRecycleBin(orgfilename);
                        _found++;
                    }
                }
                else {
                    AppFile.MoveToRecycleBin(orgfilename);
                    _found++;
                }
            }
            else {
                var orgimagedata = AppFile.ReadFile(orgfilename);
                if (orgimagedata == null) {
                    AppFile.MoveToRecycleBin(orgfilename);
                    _bad++;
                }
                else {
                    var hash = AppHash.GetHash(orgimagedata);
                    if (AppVars.Data.ContainsImg(hash)) {
                        AppFile.MoveToRecycleBin(orgfilename);
                        _found++;
                    }
                    else {
                        using var image = AppBitmap.GetImage(orgimagedata);
                        if (image == null) {
                            AppFile.MoveToRecycleBin(orgfilename);
                            _bad++;
                        }
                        else {
                            var vector = AppVit.GetVector(image);
                            if (vector == null) {
                                AppFile.MoveToRecycleBin(orgfilename);
                                _bad++;
                            }
                            else {
                                var imgnew = new Img {
                                    Hash = hash,
                                    Vector = vector,
                                    RotateMode = RotateMode.None,
                                    FlipMode = FlipMode.None,
                                    LastView = _lastview,
                                    Score = 0,
                                    LastCheck = new DateTime(1980, 1, 1),
                                    Next = string.Empty,
                                    Distance = 1f
                                };

                                AppVars.Data.AddImg(imgnew);
                                AppFile.WriteMex(hash, orgimagedata);
                                AppVars.Vectors.AddVector(hash, vector);
                                AppFile.MoveToRecycleBin(orgfilename);
                                _added++;
                                (var nextNew, var message) = AppVars.Data.GetNext(hash);
                                _lastview = _lastview.AddMinutes(-1);
                            }
                        }
                    }
                }
            }

            progress.Report($"importing {orgfilename} (a:{_added})/f:{_found}/b:{_bad}){AppConsts.CharEllipsis}");
            return true;
        }

        public void Export(IProgress<string>? progress)
        {
            progress?.Report($"Exporting{AppConsts.CharEllipsis}");
            var filename0 = Export(_imgPanels[0]!.Value.Hash);
            var filename1 = Export(_imgPanels[1]!.Value.Hash);
            progress?.Report($"Exported to {filename0} and {filename1}");
        }

        public static void Rotate(string hash, RotateMode rotatemode, FlipMode flipmode)
        {
            var imagedata = AppFile.ReadMex(hash);
            if (imagedata == null) {
                return;
            }

            using var image = AppBitmap.GetImage(imagedata, rotatemode, flipmode);
            if (image == null) {
                return;
            }

            var rvector = AppVit.GetVector(image);
            AppFile.WriteVec(hash, rvector);
            AppVars.Data.UpdateImg(hash, AppConsts.AttributeRotateMode, (int)rotatemode);
            AppVars.Data.UpdateImg(hash, AppConsts.AttributeFlipMode, (int)flipmode);
        }

        public static string Export(string hashE)
        {
            var imagedata = AppFile.ReadMex(hashE);
            if (imagedata != null) {
                var ext = AppBitmap.GetExtension(imagedata);
                var recycledName = AppFile.GetRecycledName(hashE, ext, AppConsts.PathExport, DateTime.Now);
                AppFile.CreateDirectory(recycledName);
                File.WriteAllBytes(recycledName, imagedata);
                var name = Path.GetFileName(recycledName);
                return name;
            }

            return string.Empty;
        }
    }
    */
}