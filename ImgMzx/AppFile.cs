using Microsoft.VisualBasic.FileIO;
using System.IO;
using System.Text.RegularExpressions;

namespace ImgMzx;

public static class AppFile
{
    private static readonly object _lock = new();

    private static readonly Regex _mexDeletedFileNameRegex = new(
        @"^(\d{6})\.([0-9a-z]{16})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static byte[]? ReadFile(string filename)
    {
        if (!File.Exists(filename)) {
            return null;
        }

        var ext = Path.GetExtension(filename);
        while (ext.StartsWith('.')) {
            ext = ext[1..];
        }

        byte[]? imagedata;
        imagedata = File.ReadAllBytes(filename);
        if (string.IsNullOrEmpty(ext) || ext.Equals(AppConsts.MzxExtension, StringComparison.OrdinalIgnoreCase)) {
            var password = Path.GetFileNameWithoutExtension(filename);
            imagedata = AppEncryption.Decrypt(imagedata, password);
        }
        else if (ext.Equals(AppConsts.MexExtension, StringComparison.OrdinalIgnoreCase)) {
            var password = Path.GetFileNameWithoutExtension(filename);
            // файл типа 055444.52j230emdv4ejmi6.mex (шесть цифр + точка + 16 символов), password = последние 16 символов
            // Check format: 6digits.16chars
            var match = _mexDeletedFileNameRegex.Match(password);
            if (match.Success) {
                password = match.Groups[2].Value;
            }

            imagedata = AppCrypto.Decrypt(imagedata, password);
        }

        if (imagedata == null || imagedata.Length < 16) {
            return null;
        }

        return imagedata;
    }

    public static void CreateDirectory(string filename)
    {
        var directory = Path.GetDirectoryName(filename);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
            Directory.CreateDirectory(directory);
        }
    }

    public static string GetFileName(string name, string hp)
    {
        name = name.ToLowerInvariant();
        return $"{hp}\\{name[0]}\\{name[1]}\\{name}";
    }

    public static string GetRecycledName(string name, string ext, string gb, DateTime now)
    {
        string result;
        var counter = 0;
        do {
            result = $"{gb}\\{now.Year}-{now.Month:D2}-{now.Day:D2}\\{now.Hour:D2}{now.Minute:D2}{now.Second:D2}.{name}";
            if (counter > 0) {
                result += $"({counter})";
            }

            if (!string.IsNullOrEmpty(ext)) {
                result += $".{ext}";
            }

            counter++;
        }
        while (File.Exists(result));
        return result;
    }

    public static string GetFileName(string hash, string rootpath, string extension)
    {
        var name = hash.ToLowerInvariant();
        return $"{rootpath}\\{name[0]}\\{name[1]}\\{name}.{extension}";
    }

    public static string GetDeletedName(string hash, DateTime now, string deletedpath = AppConsts.PathDeleted)
    {
        var name = hash.ToLowerInvariant();
        return $"{deletedpath}\\{now.Year}-{now.Month:D2}-{now.Day:D2}\\{now.Hour:D2}{now.Minute:D2}{now.Second:D2}.{hash}.{AppConsts.MexExtension}";
    }

    private static void CreateDirectorySafe(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory) || Directory.Exists(directory)) {
            return;
        }

        var pathsToCreate = new List<string>();
        var currentPath = directory;
        
        while (!string.IsNullOrEmpty(currentPath) && !Directory.Exists(currentPath)) {
            pathsToCreate.Add(currentPath);
            currentPath = Path.GetDirectoryName(currentPath);
        }

        // Create directories from parent to child
        for (var i = pathsToCreate.Count - 1; i >= 0; i--) {
            Directory.CreateDirectory(pathsToCreate[i]);
        }
    }

    public static void MoveToRecycleBin(string filename)
    {
        if (!File.Exists(filename)) {
            return;
        }

        try {
            FileSystem.DeleteFile(filename, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        catch {
            File.Delete(filename);
        }
    }

    public static void WriteMex(string hash, byte[] imagedata, string rootpath = AppConsts.PathHp, string backuppath = AppConsts.PathHpBackup)
    {
        if (!AppHash.IsValidHash(hash)) {
            throw new ArgumentException("Invalid hash", nameof(hash));
        }

        var file = GetFileName(hash:hash, rootpath:rootpath, extension:AppConsts.MexExtension);
        var backup = GetFileName(hash: hash, rootpath: backuppath, extension: AppConsts.MexExtension);
        lock (_lock) {
            var originalFile = string.Empty;
            var originalBackupFile = string.Empty;
            try {
                CreateDirectorySafe(file);
                if (File.Exists(file)) {
                    originalFile = Path.ChangeExtension(file, ".original");
                    File.Move(file, originalFile, overwrite: true);
                }

                using (var fs = new FileStream(file, FileMode.Create, FileAccess.Write)) {
                    AppCrypto.EncryptToStream(imagedata, hash, fs);
                    fs.Flush();
                }

                if (!string.IsNullOrEmpty(originalFile) && File.Exists(originalFile)) {
                    MoveToRecycleBin(originalFile);
                    originalFile = string.Empty;
                }

                CreateDirectorySafe(backup);
                if (File.Exists(backup)) {
                    originalBackupFile = Path.ChangeExtension(backup, ".original");
                    File.Move(backup, originalBackupFile, overwrite: true);
                }

                File.Copy(file, backup, overwrite: false);
                if (!string.IsNullOrEmpty(originalBackupFile) && File.Exists(originalBackupFile)) {
                    MoveToRecycleBin(originalBackupFile);
                    originalBackupFile = string.Empty;
                }
            }
            catch {
                // Rollback: restore original files
                if (!string.IsNullOrEmpty(originalFile) && File.Exists(originalFile)) {
                    try {
                        File.Move(originalFile, file, overwrite: true);
                    }
                    catch {
                    }
                }

                if (!string.IsNullOrEmpty(originalBackupFile) && File.Exists(originalBackupFile)) {
                    try {
                        File.Move(originalBackupFile, backup, overwrite: true);
                    }
                    catch {
                    }
                }

                throw;
            }
        }
    }
    public static byte[]? ReadMex(string hash, string rootpath = AppConsts.PathHp, string backuppath = AppConsts.PathHpBackup)
    {
        if (!AppHash.IsValidHash(hash)) {
            throw new ArgumentException("Invalid hash", nameof(hash));
        }

        var file = GetFileName(hash: hash, rootpath: rootpath, extension: AppConsts.MexExtension);
        var backup = GetFileName(hash: hash, rootpath: backuppath, extension: AppConsts.MexExtension);
        lock (_lock) {
            if (!File.Exists(file)) {
                return null;
            }

            byte[]? imagedata;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read)) {
                imagedata = AppCrypto.DecryptFromStream(fs, hash);
            }

            if (imagedata != null && imagedata.Length > 16) {
                var readHash = AppHash.GetHash(imagedata);
                if (readHash.Equals(hash)) {
                    return imagedata;
                }
            }

            if (!File.Exists(backup)) {
                return null;
            }

            using (var fs = new FileStream(backup, FileMode.Open, FileAccess.Read)) {
                imagedata = AppCrypto.DecryptFromStream(fs, hash);
            }

            if (imagedata != null && imagedata.Length > 16) {
                var readHash = AppHash.GetHash(imagedata);
                if (readHash.Equals(hash)) {
                    File.Copy(backup, file, overwrite: true);
                    return imagedata;
                }
            }

            return null;
        }
    }

    public static void DeleteMex(string hash, DateTime now, string rootpath = AppConsts.PathHp, string backuppath = AppConsts.PathHpBackup, string deletedpath = AppConsts.PathDeleted)
    {
        if (!AppHash.IsValidHash(hash)) {
            throw new ArgumentException("Invalid hash", nameof(hash));
        }

        var mex = GetFileName(hash: hash, rootpath: rootpath, extension: AppConsts.MexExtension);
        var bakmex = GetFileName(hash: hash, rootpath: backuppath, extension: AppConsts.MexExtension);
        var recycled = GetDeletedName(hash, now, deletedpath);
        lock (_lock) {
            if (File.Exists(mex)) {
                CreateDirectorySafe(recycled);
                File.Move(mex, recycled, overwrite: true);

                if (File.Exists(bakmex)) {
                    MoveToRecycleBin(bakmex);
                }
            }
            else {
                if (File.Exists(bakmex)) {
                    CreateDirectorySafe(recycled);
                    File.Move(bakmex, recycled, overwrite: true);
                }
            }
        }
    }
}