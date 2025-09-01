using SixLabors.ImageSharp.Processing;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Reflection;
using System.Text;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace ImgMzx;

public static partial class ImgMdf
{
    private static int _added;
    private static int _bad;
    private static int _found;

    private static bool ImportFile(string orgfilename, DateTime lastview, BackgroundWorker backgroundworker)
    {
        var orgname = Path.GetFileNameWithoutExtension(orgfilename).ToLowerInvariant();
        if (AppImgs.TryGetByName(orgname, out var imgE)) {
            Debug.Assert(imgE != null);
            var filenameF = AppFile.GetFileName(imgE.Name, AppConsts.PathHp);
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
        string hash;
        if (string.IsNullOrEmpty(orgext) || orgext.Equals(AppConsts.MzxExtension, StringComparison.OrdinalIgnoreCase)) {
            var decrypteddata = AppEncryption.Decrypt(imagedata, orgname);
            if (decrypteddata == null) {
                DeleteFile(orgfilename);
                _bad++;
                return true;
            }

            hash = AppHash.GetHash(decrypteddata);
            if (AppImgs.TryGet(hash, out var imgF)) {
                Debug.Assert(imgF != null);
                var filenameF = AppFile.GetFileName(imgF.Name, AppConsts.PathHp);
                if (File.Exists(filenameF)) {
                    // we have a file
                    var imagedataF = AppFile.ReadEncryptedFile(filenameF);
                    Debug.Assert(imagedataF != null);
                    var foundhash = AppHash.GetHash(imagedataF);
                    if (hash.Equals(foundhash)) {
                        // ...and file is okay
                        // delete incoming file
                        File.Delete(orgfilename);
                        _found++;
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
            if (AppImgs.TryGet(hash, out var imgF)) {
                Debug.Assert(imgF != null);
                var filenameF = AppFile.GetFileName(imgF.Name, AppConsts.PathHp);
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
                        return true;
                    }
                }

                // ...but found file is missing or changed
                // delete record with changed file and continue
                Delete(hash);
            }
        }

        using var image = AppBitmap.GetImage(imagedata);
        if (image == null) {
            DeleteFile(orgfilename);
            _bad++;
            return true;
        }

        var vector = AppVit.GetVector(image);
        var name = AppImgs.GetName(hash);
        var newfilename = AppFile.GetFileName(name, AppConsts.PathHp);
        if (!orgfilename.Equals(newfilename)) {
            AppFile.WriteEncryptedFile(newfilename, imagedata);
            File.SetLastWriteTime(newfilename, lastmodified);
            File.SetAttributes(orgfilename, FileAttributes.Normal);
            File.Delete(orgfilename);
        }
        
        var imgnew = new Img(
            hash: hash,
            name: name,
            vector: vector,
            rotatemode: RotateMode.None,
            flipmode: FlipMode.None,
            lastview: lastview,
            score: 0,
            lastcheck: new DateTime(1980, 1, 1),
            next: string.Empty,
            id: 0
        );

        AppImgs.Add(imgnew);
        AppDatabase.Add(imgnew);
        _added++;
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
            var lastview = AppImgs.GetMinimalLastView();
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

        var img = AppImgs.GetImgForCheck();
        Debug.Assert(img != null);

        var filename = AppFile.GetFileName(img.Name, AppConsts.PathHp);
        Debug.Assert(filename != null);
        var imagedata = AppFile.ReadEncryptedFile(filename);
        if (imagedata == null) {
            backgroundworker.ReportProgress(0, $"{img.Name}: imagedata = null");
            Delete(img.Hash);
            return;
        }
        
        var hash = AppHash.GetHash(imagedata);
        if (!hash.Equals(img.Hash) || img.GetVector().Length != AppConsts.VectorSize) {
            //backgroundworker.ReportProgress(0, $"{img.Name}: fixing...");
            using var image = AppBitmap.GetImage(imagedata);
            if (image == null) {
                backgroundworker.ReportProgress(0, $"{img.Name}: image == null");
                Delete(img.Hash);
                return;
            }

            var vector = AppVit.GetVector(image);
            if (!hash.Equals(img.Hash)) {
                var imgnew = new Img(
                    hash: hash,
                    name: img.Name,
                    vector: vector,
                    rotatemode: img.RotateMode,
                    flipmode: img.FlipMode,
                    lastview: img.LastView,
                    score: img.Score,
                    lastcheck: img.LastCheck,
                    next: string.Empty,
                    id: img.Id
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

        var score = 0;
        for (score = 0; score < beam.Count; score++) {
            if (beam[score].Item1.Equals(img.Next)) {
                break;
            }
        }

        if (score >= beam.Count || score != img.Score) {
            score = 0;
        }

        var hashY = beam[score].Item1;
        if (!AppImgs.TryGet(hashY, out var imgY)) {
            throw new Exception("Failed to get image by hash.");
        }

        if (imgY == null) {
            throw new Exception("Failed to get image by hash.");
        }

        if (!img.Next.Equals(imgY.Hash) || score != img.Score) {
            var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(img.LastCheck));
            var old = img.Next.Length > 4? img.Next.Substring(0, 4) : img.Next;
            var upd = imgY.Hash.Length > 4? imgY.Hash.Substring(0, 4) : imgY.Hash;
            var message = $" [{lastcheck} ago] {img.Name}: {old}[{img.Score}] {AppConsts.CharRightArrow} {upd}[{score}]";
            backgroundworker?.ReportProgress(0, message);
            img.SetNext(imgY.Hash);
            img.SetScore(score);
        }

        /*
        else {
            var nId = AppImgs.CheckCluster(img, beam);
            var oId = img.Id;
            if (oId != nId) {
                var oP = AppImgs.GetPopulation(oId);
                img.SetId(nId);
                var nP = AppImgs.GetPopulation(nId);
                var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(img.LastCheck));
                var message = $" [{lastcheck} ago] {img.Name}: {oId:D5} [{oP}] {AppConsts.CharRightArrow} {nId:D5} [{nP}]";
                backgroundworker?.ReportProgress(0, message);
            }
        }
        */

        img.UpdateLastCheck();
    }
}