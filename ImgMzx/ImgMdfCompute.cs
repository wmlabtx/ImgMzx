using SixLabors.ImageSharp.Processing;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Reflection;
using System.Text;

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
            distance: 1f,
            score: 0,
            lastcheck: new DateTime(1980, 1, 1),
            history: string.Empty,
            key: string.Empty,
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
                    verified: img.Verified,
                    next: string.Empty,
                    distance: 1f,
                    score: img.Score,
                    lastcheck: img.LastCheck,
                    history: img.History,
                    key: img.Key,
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

        var historySet = new SortedSet<string>();
        if (img.History.Length > 0) {
            var historyItems = img.History.Split(',');
            foreach (var e in historyItems) {
                if (AppImgs.TryGetByName(e, out _)) {
                    historySet.Add(e);
                }
            }

            var historyNew = string.Join(',', historySet.ToArray());
            if (!historyNew.Equals(img.History)) {
                backgroundworker?.ReportProgress(0, $"{img.Name}: updating history");
                img.SetHistory(historyNew);
            }
        }

        var beam = AppImgs.GetBeam(img);
        var i = 0;
        while (i < beam.Count) {
            if (!historySet.Contains(beam[i].Item1)) {
                break;
            }

            i++;
        }

        var next = beam[i].Item1;
        var distance = beam[i].Item2;
        var olddistance = img.Distance;
        var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(img.LastCheck));
        if (!img.Next.Equals(next) || Math.Abs(img.Distance - distance) >= 0.0001f) {
            backgroundworker?.ReportProgress(0, $"[{lastcheck} ago] {img.Name}: {olddistance:F4} {AppConsts.CharRightArrow} {distance:F4}");
            img.SetNext(next);
            img.SetDistance(distance);
        }

        (Img nImg, int nId) = AppImgs.UpdateClusters(img, beam);
        var oId = nImg.Id;
        if (oId != nId) {
            var oP = AppImgs.GetPopulation(nImg.Id);
            nImg.SetId(nId);
            var nP = AppImgs.GetPopulation(nId);
            var cpop = AppImgs.CheckForEmptyClusters();
            var message = $"#{cpop.First().Item1}({cpop.First().Item2}) / #{cpop.Last().Item1}({cpop.Last().Item2})";
            message += $" [{lastcheck} ago] {img.Name}: {oId} [{oP}] {AppConsts.CharRightArrow} {nId} [{nP}]";
            backgroundworker?.ReportProgress(0, message);
        }

        img.UpdateLastCheck();
    }
}