using SixLabors.ImageSharp.Processing;
using System.IO;

namespace ImgMzx;

public partial class Images : IDisposable
{
    private bool ImportFile(string orgfilename, ref DateTime lastview, ref int added, ref int found, ref int bad, IProgress<string>? progress)
    {
        var orgname = Path.GetFileNameWithoutExtension(orgfilename);
        var hashByName = orgname.ToLowerInvariant();
        if (AppHash.IsValidHash(hashByName) && ContainsImg(hashByName)) {
            var imagedata = AppFile.ReadMex(hashByName);
            if (imagedata == null) {
                var orgimagedata = AppFile.ReadFile(orgfilename);
                if (orgimagedata == null) {
                    AppFile.MoveToRecycleBin(orgfilename);
                    bad++;
                }
                else {
                    AppFile.WriteMex(hashByName, orgimagedata);
                    AppFile.MoveToRecycleBin(orgfilename);
                    found++;
                }
            }
            else {
                AppFile.MoveToRecycleBin(orgfilename);
                found++;
            }
        }
        else {
            var orgimagedata = AppFile.ReadFile(orgfilename);
            if (orgimagedata == null) {
                AppFile.MoveToRecycleBin(orgfilename);
                bad++;
            }
            else {
                var hash = AppHash.GetHash(orgimagedata);
                if (ContainsImg(hash)) {
                    AppFile.MoveToRecycleBin(orgfilename);
                    found++;
                }
                else {
                    using var image = AppBitmap.GetImage(orgimagedata);
                    if (image == null) {
                        AppFile.MoveToRecycleBin(orgfilename);
                        bad++;
                    }
                    else {
                        var vector = _vit.CalculateVector(image);
                        if (vector == null) {
                            AppFile.MoveToRecycleBin(orgfilename);
                            bad++;
                        }
                        else {
                            var imgnew = new Img(
                                hash: hash,
                                rotateMode: RotateMode.None,
                                flipMode: FlipMode.None,
                                lastView: lastview,
                                score: 0,
                                lastCheck: new DateTime(1980, 1, 1),
                                next: string.Empty,
                                distance: 1f,
                                history: string.Empty,
                                images: this);

                            AddImgToDatabase(imgnew, vector.AsSpan());
                            AppFile.WriteMex(hash, orgimagedata);
                            AppFile.MoveToRecycleBin(orgfilename);
                            added++;
                            var message = GetNext(hash);
                            lastview = lastview.AddMinutes(-1);
                        }
                    }
                }
            }
        }

        progress?.Report($"importing {orgfilename} (a:{added})/f:{found}/b:{bad}){AppConsts.CharEllipsis}");
        return true;
    }

    private void ImportFiles(string path, SearchOption so, ref DateTime lastview, ref int added, ref int found, ref int bad, IProgress<string>? progress)
    {
        var directoryInfo = new DirectoryInfo(path);
        var fs = directoryInfo.GetFiles("*.*", so).ToArray();
        foreach (var e in fs) {
            var orgfilename = e.FullName;
            if (!ImportFile(orgfilename, ref lastview, ref added, ref found, ref bad, progress)) {
                break;
            }

            if (added >= AppConsts.MaxImportFiles) {
                break;
            }
        }

        progress?.Report($"clean-up {path}{AppConsts.CharEllipsis}");
        Helper.CleanupDirectories(path, progress);
    }
    public void Import(IProgress<string>? progress)
    {
        MaxImages -= 100;
        var lastview = GetLastView();
        var added = 0;
        var found = 0;
        var bad = 0;
        ImportFiles(AppConsts.PathRawProtected, SearchOption.TopDirectoryOnly, ref lastview, ref added, ref found, ref bad, progress);
        if (added < AppConsts.MaxImportFiles) {
            var directoryInfo = new DirectoryInfo(AppConsts.PathRawProtected);
            var ds = directoryInfo.GetDirectories("*.*", SearchOption.TopDirectoryOnly).ToArray();
            foreach (var di in ds) {
                ImportFiles(di.FullName, SearchOption.AllDirectories, ref lastview, ref added, ref found, ref bad, progress);
                if (added >= AppConsts.MaxImportFiles) {
                    break;
                }
            }
        }

        Helper.CleanupDirectories(AppConsts.PathRawProtected, progress);
        progress?.Report($"Imported a:{added}/f:{found}/b:{bad}");
    }
}
