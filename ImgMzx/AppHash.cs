using System.Security.Cryptography;

namespace ImgMzx;

public static class AppHash
{
    public static string GetHash(byte[] data)
    {
        var buffer = SHA256.HashData(data);
        var hash = Convert.ToHexString(buffer);
        return hash;
    }
}