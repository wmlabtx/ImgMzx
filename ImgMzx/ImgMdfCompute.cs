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
        var hash = orgname.ToUpperInvariant();
        if (AppDatabase.ContainsKey(hash)) {
            var filenameF = AppFile.GetFileName(orgname, AppConsts.PathHp);
            if (orgfilename.Equals(filenameF)) {
                // existing file
                return true;
            }
        }

        var orgext = Path.GetExtension(orgfilename);
        while (orgext.StartsWith(".")) {
            orgext = orgext[1..];
        }

        backgroundworker.ReportProgress(0,
            $"importing {orgfilename} (a:{_added})/f:{_found}/b:{_bad}){AppConsts.CharEllipsis}");
        var lastmodified = File.GetLastWriteTime(orgfilename);
        if (lastmodified > DateTime.Now) {
            lastmodified = DateTime.Now;
        }

        var imagedata = AppFile.ReadFile(orgfilename);
        Debug.Assert(imagedata != null);
        if (string.IsNullOrEmpty(orgext) || orgext.Equals(AppConsts.MzxExtension, StringComparison.OrdinalIgnoreCase)) {
            var decrypteddata = AppEncryption.Decrypt(imagedata, orgname);
            if (decrypteddata == null) {
                DeleteFile(orgfilename);
                _bad++;
                return true;
            }

            hash = AppHash.GetHash(decrypteddata);
            if (AppDatabase.ContainsKey(hash)) {
                var filenameF = AppFile.GetFileName(hash, AppConsts.PathHp);
                if (File.Exists(filenameF)) {
                    // we have a file
                    var imagedataF = AppFile.ReadEncryptedFile(filenameF);
                    Debug.Assert(imagedataF != null);
                    var foundhash = AppHash.GetHash(imagedataF);
                    if (hash.Equals(foundhash)) {
                        // ...and file is okay
                        if (!orgfilename.Equals(filenameF)) {
                            // delete incoming file
                            File.Delete(orgfilename);
                            _found++;
                        }

                        return true;
                    }
                }

                // ...but found file is missing or changed
                // delete record with changed file and continue
                Delete(hash);
            }

            var tmporgfilename = $"{AppConsts.PathGbProtected}\\{orgname}.temp";
            File.WriteAllBytes(tmporgfilename, decrypteddata);
            File.Delete(tmporgfilename);
            imagedata = decrypteddata;
        }
        else {
            hash = AppHash.GetHash(imagedata);
            if (AppDatabase.ContainsKey(hash)) {
                var filenameF = AppFile.GetFileName(hash, AppConsts.PathHp);
                if (File.Exists(filenameF)) {
                    // we have a file
                    var imagedataF = AppFile.ReadEncryptedFile(filenameF);
                    Debug.Assert(imagedataF != null);
                    var foundhash = AppHash.GetHash(imagedataF);
                    if (hash.Equals(foundhash)) {
                        // ...and file is okay
                        // delete incoming file
                        File.SetAttributes(orgfilename, FileAttributes.Normal);
                        File.Delete(orgfilename);
                        _found++;
                    }

                    return true;
                }

                // ...but found file is missing or changed
                // delete record with changed file and continue
                Delete(hash);
            }
        }

        using var image = AppBitmap.GetImage(imagedata, RotateMode.None, FlipMode.None);
        if (image == null) {
            DeleteFile(orgfilename);
            _bad++;
            return true;
        }

        var vector = AppVit.GetVector(image);
        var newfilename = AppFile.GetFileName(hash, AppConsts.PathHp);

        if (!orgfilename.Equals(newfilename)) {
            AppFile.WriteEncryptedFile(newfilename, imagedata);
            File.SetLastWriteTime(newfilename, lastmodified);
            File.SetAttributes(orgfilename, FileAttributes.Normal);
            File.Delete(orgfilename);
        }

        var imgnew = new Img {
            RotateMode = RotateMode.None,
            FlipMode = FlipMode.None,
            LastView = lastview,
            Score = 0,
            LastCheck = new DateTime(1980, 1, 1),
            Next = string.Empty,
            Distance = 2f
        };

        AppDatabase.Add(hash, imgnew, vector);
        _added++;
        (var nextNew, var message) = AppDatabase.GetNext(hash);
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
            ImportFiles(AppConsts.PathHp, SearchOption.AllDirectories, lastview, backgroundworker);
            if (_added < AppConsts.MaxImportFiles) {
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
            }

            AppVars.ImportRequested = false;
            if (AppVars.Progress != null) {
                Helper.CleanupDirectories(AppConsts.PathRawProtected, AppVars.Progress);
                ((IProgress<string>)AppVars.Progress).Report($"Imported a:{_added}/f:{_found}/b:{_bad}");
            }
        }

        /*
        var img = AppImgs.GetImgForCheck();
        Debug.Assert(img != null);

        var filename = AppFile.GetFileName(img.Hash, AppConsts.PathHp);
        Debug.Assert(filename != null);
        var imagedata = AppFile.ReadEncryptedFile(filename);
        if (imagedata == null) {
            backgroundworker.ReportProgress(0, $"{img.Hash.Substring(0, 4)}: imagedata = null");
            Delete(img.Hash);
            return;
        }
        
        var hash = AppHash.GetHash(imagedata);
        if (!hash.Equals(img.Hash) || img.GetVector().Length != AppConsts.VectorSize) {
            //backgroundworker.ReportProgress(0, $"{img.Name}: fixing...");
            using var image = AppBitmap.GetImage(imagedata);
            if (image == null) {
                backgroundworker.ReportProgress(0, $"{img.Hash.Substring(0, 4)}: image == null");
                Delete(img.Hash);
                return;
            }

            var vector = AppVit.GetVector(image);
            if (!hash.Equals(img.Hash)) {
                var imgnew = new Img(
                    hash: hash,
                    vector: vector,
                    rotatemode: img.RotateMode,
                    flipmode: img.FlipMode,
                    lastview: img.LastView,
                    score: img.Score,
                    lastcheck: img.LastCheck,
                    next: string.Empty,
                    distance: 2f
                );

                AppImgs.Delete(img.Hash);
                AppDatabase.Delete(img.Hash);
                AppImgs.Add(imgnew);
                AppDatabase.Add(imgnew);
                img = imgnew;
            }
            else {
                if (img.GetVector().Length != AppConsts.VectorSize) {
                    img.SetVector(vector);
                }
            }
        }

        var beam = AppImgs.GetBeam(img);
        if (beam.Count == 0) {
            throw new Exception("No images found for beam.");
        }

        var hs = AppImgs.GetPairs(img.Hash);
        Img? imgY = null;
        var index = 0;
        for (index = 0; index < beam.Count; index++) {
            if (!hs.Contains(beam[index].Item1)) {
                var hashY = beam[index].Item1;
                if (!AppImgs.TryGet(hashY, out imgY)) {
                    continue;
                }

                break;
            }
        }

        if (imgY == null) {
            throw new Exception();
        }

        if (!img.Next.Equals(imgY.Hash) || Math.Abs(img.Distance - beam[index].Item2) >= 0.0001f) {
            var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(img.LastCheck));
            var message = $" [{lastcheck} ago] {img.Hash.Substring(0, 4)}: {img.Distance:F4} {AppConsts.CharRightArrow} {beam[index].Item2:F4}";
            backgroundworker?.ReportProgress(0, message);
            img.SetNext(imgY.Hash);
            img.SetDistance(beam[index].Item2);
        }

        img.UpdateLastCheck();
        */
    }
}