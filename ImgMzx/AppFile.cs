using System.IO;

namespace ImgMzx;

public static class AppFile
{
    public static byte[]? ReadEncryptedFile(string filename)
    {
        if (!File.Exists(filename)) {
            return null;
        }

        var encryptedArray = ReadFile(filename);
        if (encryptedArray == null) {
            return null;
        }

        var password = Path.GetFileNameWithoutExtension(filename);
        var data = AppEncryption.Decrypt(encryptedArray, password);
        return data;
    }

    public static byte[]? ReadFile(string filename)
    {
        if (!File.Exists(filename)) {
            return null;
        }

        var data = File.ReadAllBytes(filename);
        return data;
    }

    public static void CreateDirectory(string filename)
    {
        var directory = Path.GetDirectoryName(filename);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
            Directory.CreateDirectory(directory);
        }
    }

    public static void WriteFile(string filename, byte[] data)
    {
        CreateDirectory(filename);
        File.WriteAllBytes(filename, data);
    }

    public static void WriteEncryptedFile(string filename, byte[] data)
    {
        var password = Path.GetFileNameWithoutExtension(filename);
        var encryptedArray = AppEncryption.Encrypt(data, password);
        CreateDirectory(filename);
        File.WriteAllBytes(filename, encryptedArray);
    }

    public static string GetFileName(string name, string hp)
    {
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
}