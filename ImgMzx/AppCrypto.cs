using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.IO;

namespace ImgMzx;

public static class AppCrypto
{
    private const int KeySize = 32;      // 256-bit
    private const int NonceSize = 12;    // GCM standard
    private const int TagSize = 16;      // 128-bit tag

    private static readonly byte[] PasswordSalt = "{mex}"u8.ToArray();

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static byte[] Encrypt(ReadOnlySpan<byte> data, ReadOnlySpan<char> password)
    {
        var key = DeriveKeyBytes(password);
        try
        {
            // result = [Nonce (12) | Ciphertext (n) | Tag (16)]
            var result = new byte[NonceSize + data.Length + TagSize];
            var resultSpan = result.AsSpan();

            // Nonce
            var nonce = resultSpan[..NonceSize];
            RandomNumberGenerator.Fill(nonce);

            // Outputs
            var ciphertext = resultSpan.Slice(NonceSize, data.Length);
            var tag = resultSpan.Slice(NonceSize + data.Length, TagSize);

            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, data, ciphertext, tag);

            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static byte[]? Decrypt(ReadOnlySpan<byte> encryptedData, ReadOnlySpan<char> password)
    {
        if (encryptedData.Length < NonceSize + TagSize)
            return null;

        // Parse header: [nonce|cipher|tag]
        var nonce = encryptedData[..NonceSize];
        var cipherLen = encryptedData.Length - NonceSize - TagSize;
        if (cipherLen < 0) return null;

        var ciphertext = encryptedData.Slice(NonceSize, cipherLen);
        var tag = encryptedData.Slice(NonceSize + cipherLen, TagSize);

        var key = DeriveKeyBytes(password);
        try
        {
            var result = new byte[cipherLen];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, result);
            return result;
        }
        catch (CryptographicException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Encrypts to an output stream without extra copies.
    /// Output format: [nonce|cipher|tag].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void EncryptToStream(ReadOnlySpan<byte> data, ReadOnlySpan<char> password, Stream outputStream)
    {
        var key = DeriveKeyBytes(password);
        var cipher = ArrayPool<byte>.Shared.Rent(data.Length);
        Span<byte> nonce = stackalloc byte[NonceSize];
        Span<byte> tag = stackalloc byte[TagSize];

        try
        {
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, data, cipher.AsSpan(0, data.Length), tag);

            outputStream.Write(nonce);
            outputStream.Write(cipher, 0, data.Length);
            outputStream.Write(tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            Array.Clear(cipher, 0, data.Length);
            ArrayPool<byte>.Shared.Return(cipher, clearArray: false);
        }
    }

    /// <summary>
    /// Reads all from inputStream and decrypts (supports non-seekable streams).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static byte[]? DecryptFromStream(Stream inputStream, ReadOnlySpan<char> password)
    {
        using var ms = new MemoryStream(capacity: 16 * 1024);
        inputStream.CopyTo(ms);
        var buffer = ms.GetBuffer();
        var length = (int)ms.Length;
        return Decrypt(buffer.AsSpan(0, length), password);
    }

    public static void EncryptBatch(IReadOnlyList<byte[]> inputs, IReadOnlyList<string> passwords, IList<byte[]> outputs)
    {
        if (inputs.Count != passwords.Count || inputs.Count != outputs.Count)
            throw new ArgumentException("All collections must have the same count");

        Parallel.For(0, inputs.Count, i =>
        {
            outputs[i] = Encrypt(inputs[i], passwords[i]);
        });
    }

    public static void DecryptBatch(IReadOnlyList<byte[]> inputs, IReadOnlyList<string> passwords, IList<byte[]?> outputs)
    {
        if (inputs.Count != passwords.Count || inputs.Count != outputs.Count)
            throw new ArgumentException("All collections must have the same count");

        Parallel.For(0, inputs.Count, i =>
        {
            outputs[i] = Decrypt(inputs[i], passwords[i]);
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] DeriveKeyBytes(ReadOnlySpan<char> password)
    {
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(password);
        var pwdBytes = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            System.Text.Encoding.UTF8.GetBytes(password, pwdBytes.AsSpan(0, byteCount));

            var key = new byte[KeySize];
            Rfc2898DeriveBytes.Pbkdf2(
                password: pwdBytes.AsSpan(0, byteCount),
                salt: PasswordSalt,
                iterations: 100_000,
                hashAlgorithm: HashAlgorithmName.SHA256,
                destination: key);

            CryptographicOperations.ZeroMemory(pwdBytes.AsSpan(0, byteCount));
            return key;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pwdBytes, clearArray: false);
        }
    }

    public static class Performance
    {
        public const int EstimatedEncryptionMBps = 500;
        public const int OptimalBlockSize = 4 * 1024 * 1024;

        public static class HardwareSupport
        {
            public static bool AesNi => System.Runtime.Intrinsics.X86.Aes.IsSupported;
            public static bool Avx2 => System.Runtime.Intrinsics.X86.Avx2.IsSupported;
            public static bool Vector128 => System.Numerics.Vector.IsHardwareAccelerated && System.Numerics.Vector<byte>.Count >= 16;
            public static bool Vector256 => System.Numerics.Vector.IsHardwareAccelerated && System.Numerics.Vector<byte>.Count >= 32;

            public static string GetOptimalPath()
            {
                if (AesNi && Avx2) return "AES-NI + AVX2 (optimal)";
                if (AesNi) return "AES-NI (hardware accelerated)";
                if (Vector128) return "Vector128 (SIMD accelerated)";
                return "Software implementation";
            }
        }
    }
}

public static class AppCryptoExtensions
{
    public static byte[] EncryptFromFile(this string filePath, string password)
    {
        var data = File.ReadAllBytes(filePath);
        return AppCrypto.Encrypt(data, password);
    }

    public static bool DecryptToFile(this byte[] encryptedData, string password, string outputPath)
    {
        var decrypted = AppCrypto.Decrypt(encryptedData, password);
        if (decrypted == null) return false;
        File.WriteAllBytes(outputPath, decrypted);
        return true;
    }

    public static void EncryptFileToFile(this string inputFilePath, string password, string outputFilePath)
    {
        var data = File.ReadAllBytes(inputFilePath);
        using var outputStream = File.Create(outputFilePath);
        AppCrypto.EncryptToStream(data, password, outputStream);
    }

    public static bool DecryptFileToFile(this string inputFilePath, string password, string outputFilePath)
    {
        try
        {
            using var inputStream = File.OpenRead(inputFilePath);
            var decrypted = AppCrypto.DecryptFromStream(inputStream, password);
            if (decrypted == null) return false;
            File.WriteAllBytes(outputFilePath, decrypted);
            return true;
        }
        catch
        {
            return false;
        }
    }
}