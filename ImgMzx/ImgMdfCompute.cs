using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using SixLabors.ImageSharp.Processing;

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
            verified: false,
            next: string.Empty,
            distance: float.MaxValue,
            score: 0,
            lastcheck: new DateTime(1980, 1, 1)
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
            AppVars.MaxImages = (int)Math.Round(AppVars.MaxImages * 0.9996);
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
/*
        var img = AppImgs.GetForCheck();
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
        if (!hash.Equals(img.Hash) || img.GetVector().Length != 512) {
            backgroundworker.ReportProgress(0, $"{img.Name}: fixing...");
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
                    history: img.GetHistory(),
                    rotatemode: img.RotateMode,
                    flipmode: img.FlipMode,
                    lastview: img.LastView,
                    verified: img.Verified,
                    next: string.Empty,
                    distance: float.MaxValue,
                    score: img.Score,
                    lastcheck: img.LastCheck
                );

                AppImgs.Delete(img.Hash);
                AppDatabase.Delete(img.Hash);
                AppImgs.Add(imgnew);
                AppDatabase.Add(imgnew);
                img = imgnew;
            }
            else {
                if (img.GetVector().Length != 512) {
                    img.SetVector(vector);
                }
            }
        }

        var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(img.LastCheck));
        backgroundworker?.ReportProgress(0, $"[{lastcheck} ago] {img.Name}");
        img.UpdateLastCheck();

        /*
        var history = img.GetHistory();
        foreach (var h in history) {
            if (!AppImgs.TryGetByName(h, out var imgnext)) {
                img.RemoveFromHistory(h);
            }
        }

        var beam = AppImgs.GetBeam(img);
        var next = beam[0].Item1.Hash;
        var distance = beam[0].Item2;
        var olddistance = img.Distance;
        var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(img.LastCheck));
        if (!img.Next.Equals(next) || Math.Abs(img.Distance - distance) >= 0.0001f) {
            Debug.Assert(!img.Next.Equals(hash));
            backgroundworker?.ReportProgress(0, $"[{lastcheck} ago] {img.Name}: {olddistance:F4} {AppConsts.CharRightArrow} {distance:F4}");
            img.SetNext(next);
            img.SetDisnance(distance);
        }

        img.UpdateLastCheck();
*/
    }
}