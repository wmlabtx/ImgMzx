using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ImgMzx
{
    public static class AppEncryption
    {
        private const string PasswordSole = "{mzx}";
#pragma warning disable IDE0300
        private static readonly byte[] AesIv = {
            0xE1, 0xD9, 0x94, 0xE6, 0xE6, 0x43, 0x39, 0x34,
            0x33, 0x0A, 0xCC, 0x9E, 0x7D, 0x66, 0x97, 0x16
        };
#pragma warning restore IDE0300

        private static Aes CreateAes(string password)
        {
            using var hash256 = SHA256.Create();
            var passwordWithSole = string.Concat(password, PasswordSole);
            var passwordBuffer = Encoding.ASCII.GetBytes(passwordWithSole);
            var passwordKey256 = SHA256.HashData(passwordBuffer);
            var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Key = passwordKey256;
            aes.BlockSize = 128;
            aes.IV = AesIv;
            aes.Mode = CipherMode.CBC;
            return aes;
        }

        public static byte[]? Decrypt(byte[] array, string password)
        {
            using var aes = CreateAes(password);
            try {
                using var ms = new MemoryStream(array);
                using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
                using var dms = new MemoryStream();
                cs.CopyTo(dms);
                return dms.ToArray();
            }
            catch (CryptographicException) {
                return null;
            }
        }

        public static byte[] Encrypt(byte[] array, string password)
        {
            using var aes = CreateAes(password);
            using var ms = new MemoryStream();
            using var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
            cs.Write(array, 0, array.Length);
            cs.FlushFinalBlock();
            return ms.ToArray();
        }
    }
}
