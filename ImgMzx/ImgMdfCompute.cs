using SixLabors.ImageSharp.Processing;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Reflection;
using System.Security.Policy;
using System.Text;
using System.Windows.Documents;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace ImgMzx;

public static partial class ImgMdf
{
    private static int _added;
    private static int _bad;
    private static int _found;

    private static bool ImportFile(string orgfilename, DateTime lastview, BackgroundWorker backgroundworker)
    {
        var orgname = Path.GetFileNameWithoutExtension(orgfilename);
        var hashByName = orgname.ToUpperInvariant();
        if (AppDatabase.ContainsKey(hashByName)) {
            var imagedata = AppDatabase.ImgReadContent(hashByName);
            if (imagedata.Length > 16) {
                AppFile.DeleteFile(orgfilename);
                _found++;
            }
            else {
                var orgimagedata = AppFile.ReadFile(orgfilename);
                if (orgimagedata == null) {
                    AppFile.DeleteFile(orgfilename);
                    _bad++;
                }
                else {
                    AppDatabase.ImgWriteContent(hashByName, orgimagedata);
                    AppFile.DeleteFile(orgfilename);
                    _found++;
                }
            }
        }
        else {
            var orgimagedata = AppFile.ReadFile(orgfilename);
            if (orgimagedata == null) {
                AppFile.DeleteFile(orgfilename);
                _bad++;
            }
            else {
                var hash = AppHash.GetHash(orgimagedata);
                if (AppDatabase.ContainsKey(hash)) {
                    var imagedata = AppDatabase.ImgReadContent(hashByName);
                    if (imagedata.Length > 16) {
                        AppFile.DeleteFile(orgfilename);
                        _found++;
                    }
                    else {
                        AppDatabase.ImgWriteContent(hash, orgimagedata);
                        AppFile.DeleteFile(orgfilename);
                        _found++;
                    }
                }
                else {
                    using var image = AppBitmap.GetImage(orgimagedata);
                    if (image == null) {
                        AppFile.DeleteFile(orgfilename);
                        _bad++;
                    }
                    else {
                        var vector = AppVit.GetVector(image);
                        if (vector == null) {
                            AppFile.DeleteFile(orgfilename);
                            _bad++;
                        }
                        else {
                            var imgnew = new Img {
                                RotateMode = RotateMode.None,
                                FlipMode = FlipMode.None,
                                LastView = lastview,
                                Score = 0,
                                LastCheck = new DateTime(1980, 1, 1),
                                Next = string.Empty,
                                Distance = 1f
                            };

                            AppDatabase.Add(hash, imgnew, orgimagedata, vector);
                            _added++;
                            (var nextNew, var message) = AppDatabase.GetNext(hash);
                        }
                    }
                }
            }
        }

        backgroundworker.ReportProgress(0,
            $"importing {orgfilename} (a:{_added})/f:{_found}/b:{_bad}){AppConsts.CharEllipsis}");
        return true;
    }

    private static void ImportFiles(string path, SearchOption so, DateTime lastview, BackgroundWorker backgroundworker)
    {
        var directoryInfo = new DirectoryInfo(path);
        var fs = directoryInfo.GetFiles("*.*", so).ToArray();
        foreach (var e in fs) {
            var orgfilename = e.FullName;
            if (!ImportFile(orgfilename, lastview, backgroundworker)) {
                break;
            }

            if (_added >= AppConsts.MaxImportFiles) {
                break;
            }
        }

        backgroundworker.ReportProgress(0, $"clean-up {path}{AppConsts.CharEllipsis}");
        if (AppVars.Progress != null) {
            Helper.CleanupDirectories(path, AppVars.Progress);
        }
    }

    public static void BackgroundWorker(BackgroundWorker backgroundworker)
    {
        Compute(backgroundworker);
    }

    private static void Compute(BackgroundWorker backgroundworker)
    {
        if (AppVars.ImportRequested) {
            AppVars.MaxImages = AppVars.MaxImages - 100;
            AppDatabase.UpdateMaxImages();
            var lastview = AppDatabase.GetMinimalLastView();
            _added = 0;
            _found = 0;
            _bad = 0;
            ImportFiles(AppConsts.PathRawProtected, SearchOption.TopDirectoryOnly, lastview, backgroundworker);
            if (_added < AppConsts.MaxImportFiles) {
                var directoryInfo = new DirectoryInfo(AppConsts.PathRawProtected);
                var ds = directoryInfo.GetDirectories("*.*", SearchOption.TopDirectoryOnly).ToArray();
                foreach (var di in ds) {
                    ImportFiles(di.FullName, SearchOption.AllDirectories, lastview, backgroundworker);
                    if (_added >= AppConsts.MaxImportFiles) {
                        break;
                    }
                }
            }

            AppVars.ImportRequested = false;
            if (AppVars.Progress != null) {
                Helper.CleanupDirectories(AppConsts.PathRawProtected, AppVars.Progress);
                ((IProgress<string>)AppVars.Progress).Report($"Imported a:{_added}/f:{_found}/b:{_bad}");
            }

            return;
        }

        /*
        var stopwatch = Stopwatch.StartNew();
        var hash = AppDatabase.GetLastCheck();
        var img = AppDatabase.GetImg(hash);
        var sb = new StringBuilder();
        var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(img!.Value.LastCheck));
        sb.Append($"[{lastcheck} ago] {hash.Substring(0, 4)}: ");

        if (img.Value.Hash.Length != 16) {
            var content = AppDatabase.ImgReadContent(hash);
            if (content != null && content.Length > 16) {
                var hashContent = AppHash.GetHash(content);
                using (var image = AppBitmap.GetImage(content)) {
                    if (image != null) {
                        var vector = AppVit.GetVector(image, AppVars.BatchContext!);
                        AppFile.WriteMex(hashContent, content);
                        AppFile.WriteVec(hashContent, vector);
                        AppDatabase.ImgUpdateProperty(hash, AppConsts.AttributeHash32, hashContent);
                        sb.Append($"SAVING ({img.Value.Hash32.Length})");
                    }
                }
            }
        }
        else {
            sb.Append("OK");
        }

        AppDatabase.UpdateLastCheck(hash);
        stopwatch.Stop();

        var totalTime = stopwatch.ElapsedMilliseconds;
        backgroundworker.ReportProgress(0, $"{sb} | {totalTime}ms");
        */
    }
}