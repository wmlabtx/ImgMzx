using SixLabors.ImageSharp.Processing;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ImgMzx;

public partial class Images : IDisposable
{
    private bool ImportFile(string orgfilename, ref DateTime lastview, ref int added, ref int found, ref int bad, IProgress<string>? progress)
    {
        var orgname = Path.GetFileNameWithoutExtension(orgfilename);
        var hashByName = orgname.ToLowerInvariant();
        if (AppHash.IsValidHash(hashByName) && ContainsImgInDatabase(hashByName)) {
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
                if (ContainsImgInDatabase(hash)) {
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
                        var vector = CalculateVector(image);
                        if (vector == null) {
                            AppFile.MoveToRecycleBin(orgfilename);
                            bad++;
                        }
                        else {
                            var imgnew = new Img {
                                Hash = hash,
                                RotateMode = RotateMode.None,
                                FlipMode = FlipMode.None,
                                LastView = lastview,
                                Score = 0,
                                LastCheck = new DateTime(1980, 1, 1),
                                Next = string.Empty,
                                Distance = 1f,
                                History = string.Empty
                            };

                            AddImgToDatabase(imgnew, vector);
                            AppFile.WriteMex(hash, orgimagedata);
                            AddVector(hash, vector);
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
        _maxImages -= 100;
        UpdateMaxImagesInDatabase(_maxImages);
        var lastview = GetLastViewFromDatabase() ?? DateTime.Now;
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

    /*
    private static readonly byte[] _saltBytes = { 0xFF, 0x15, 0x20, 0xD5, 0x24, 0x1E, 0x12, 0xAA, 0xCC, 0xFF };
    private const int Iterations = 1000;

    public static byte[]? DecryptDat(byte[] bytesToBeDecrypted, string password)
    {
        if (bytesToBeDecrypted == null || password == null) {
            return null;
        }

        byte[]? decryptedBytes = null;

        try {
            using (var ms = new MemoryStream())
            using (var aes = new RijndaelManaged()) {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                var passwordBytes = Encoding.ASCII.GetBytes(password);
                using (var key = new Rfc2898DeriveBytes(passwordBytes, _saltBytes, Iterations)) {
                    aes.Key = key.GetBytes(aes.KeySize / 8);
                    aes.IV = key.GetBytes(aes.BlockSize / 8);
                    aes.Mode = CipherMode.CBC;
                    using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write)) {
                        cs.Write(bytesToBeDecrypted, 0, bytesToBeDecrypted.Length);
                        cs.Flush();
                    }

                    decryptedBytes = ms.ToArray();
                }
            }
        }
        catch (CryptographicException) {
        }

        return decryptedBytes;
    }

    public void Import(IProgress<string>? progress)
    {
        var added = 0;
        var found = 0;
        var bad = 0;


        var directoryInfo = new DirectoryInfo("M:\\temp\\cinematic");
        var fs = directoryInfo.GetFiles("*.*", SearchOption.TopDirectoryOnly).ToArray();
        foreach (var e in fs) {
            var orgfilename = e.FullName;
            var orgname = Path.GetFileNameWithoutExtension(orgfilename);
            var imagedata = File.ReadAllBytes(orgfilename);
            var decrypted = DecryptDat(imagedata, orgname);
            var newfilename = Path.ChangeExtension(orgfilename, ".mkv");
            File.WriteAllBytes(newfilename, decrypted ?? imagedata);
            progress?.Report(orgname);
        }

        progress?.Report("Done");


    }
    */
}
