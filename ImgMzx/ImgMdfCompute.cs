﻿using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml.Linq;
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
            distance: 2f,
            confirmed: string.Empty,
            score: 0,
            lastcheck: lastview
        );

        AppImgs.Save(imgnew);
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
        if (!hash.Equals(img.Hash) || img.Vector.Length != 512) {
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
                    rotatemode: img.RotateMode,
                    flipmode: img.FlipMode,
                    lastview: img.LastView,
                    verified: img.Verified,
                    next: string.Empty,
                    distance: 2f,
                    confirmed: string.Empty,
                    score: img.Score,
                    lastcheck: img.LastCheck
                );

                AppImgs.Remove(img.Hash);
                AppImgs.Delete(img.Hash);
                AppImgs.Save(imgnew);
                img = imgnew;
            }
            else {
                if (img.Vector.Length != 512) {
                    AppImgs.SetVectorFacesOrientation(img.Hash, vector, img.RotateMode, img.FlipMode);
                }
            }
        }

        var beam = AppImgs.GetBeam(img);
        var index = Array.FindIndex(beam, t => !t.Item1.Hash.Equals(img.Hash));
        var next = beam[index].Item1.Hash;
        var distance = beam[index].Item2;
        var olddistance = img.Distance;
        var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(img.LastCheck));
        if (!img.Next.Equals(next) || Math.Abs(img.Distance - distance) >= 0.0001f) {
            Debug.Assert(!img.Next.Equals(hash));
            backgroundworker?.ReportProgress(0, $"[{lastcheck} ago] {img.Name}: {olddistance:F4} {AppConsts.CharRightArrow} {distance:F4}");
            img = AppImgs.SetNextDistance(img.Hash, next, distance);
        }

        Debug.Assert(img != null);
        img = AppImgs.UpdateLastCheck(img.Hash);
        Debug.Assert(img != null);

        var affected = beam
            .Where(b => !b.Item1.Hash.Equals(hash) && (b.Item1.Next.Length == 0 || b.Item2 < b.Item1.Distance))
            .ToArray();
        for (var i = 0; i < affected.Length; i++) {
            Debug.Assert(!affected[i].Item1.Hash.Equals(hash));
            backgroundworker?.ReportProgress(0, $"{i + 1}/{affected.Length} [{lastcheck} ago] {img.Name}: {olddistance:F4} {AppConsts.CharRightArrow} {distance:F4}");
            _ = AppImgs.SetNextDistance(affected[i].Item1.Hash, hash, affected[i].Item2);
        }
    }
}
