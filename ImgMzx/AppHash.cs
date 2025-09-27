using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;

namespace ImgMzx;

public static class AppHash
{
    private static readonly byte[] AlphabetLookup = "0123456789abcdefghijkmnpqrstuvxz"u8.ToArray();

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static string GetHash(ReadOnlySpan<byte> data)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        const int blockSize = 4 * 1024 * 1024;
        for (var i = 0; i < data.Length; i += blockSize) {
            var blockLength = Math.Min(blockSize, data.Length - i);
            sha256.AppendData(data.Slice(i, blockLength));
        }

        Span<byte> hashBuffer = stackalloc byte[32];
        sha256.GetHashAndReset(hashBuffer);

        return ToBase32Simd(hashBuffer.Slice(0, 10));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetHash(byte[] data) => GetHash(data.AsSpan());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ToBase32Simd(ReadOnlySpan<byte> data)
    {
        if (Avx2.IsSupported) {
            return ToBase32Avx2(data);
        }
        else {
            return ToBase32Scalar(data);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ToBase32Avx2(ReadOnlySpan<byte> data)
    {
        // Use zero-allocation path; 10-byte input does not benefit from AVX2 here
        Span<char> result = stackalloc char[16];
        var resultIndex = 0;

        for (int dataIndex = 0; dataIndex < data.Length; dataIndex += 5) {
            var bytesToProcess = Math.Min(5, data.Length - dataIndex);
            var chunk = data.Slice(dataIndex, bytesToProcess);

            ulong buffer = 0;
            for (int i = 0; i < bytesToProcess; i++) {
                buffer = (buffer << 8) | chunk[i];
            }

            var bitsUsed = bytesToProcess * 8;
            buffer <<= (40 - bitsUsed);

            var symbolsToExtract = (bitsUsed + 4) / 5;
            for (int i = symbolsToExtract - 1; i >= 0 && resultIndex < result.Length; i--) {
                var symbol = (int)((buffer >> (i * 5)) & 0x1F);
                result[resultIndex++] = (char)AlphabetLookup[symbol];
            }
        }

        while (resultIndex < 16) {
            result[resultIndex++] = '0';
        }

        return new string(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ToBase32Scalar(ReadOnlySpan<byte> data)
    {
        Span<char> result = stackalloc char[16];
        var resultIndex = 0;

        for (int dataIndex = 0; dataIndex < data.Length; dataIndex += 5)
        {
            var bytesToProcess = Math.Min(5, data.Length - dataIndex);
            var chunk = data.Slice(dataIndex, bytesToProcess);

            ulong buffer = 0;
            for (int i = 0; i < bytesToProcess; i++) {
                buffer = (buffer << 8) | chunk[i];
            }

            var bitsUsed = bytesToProcess * 8;
            buffer <<= (40 - bitsUsed);

            var symbolsToExtract = (bitsUsed + 4) / 5;
            for (int i = symbolsToExtract - 1; i >= 0 && resultIndex < result.Length; i--) {
                var symbol = (int)((buffer >> (i * 5)) & 0x1F);
                result[resultIndex++] = (char)AlphabetLookup[symbol];
            }
        }

        while (resultIndex < 16) {
            result[resultIndex++] = '0';
        }

        return new string(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValidHash(ReadOnlySpan<char> hash)
    {
        if (hash.Length != 16) {
            return false;
        }

        var validChars = "0123456789abcdefghijkmnpqrstuvxz".AsSpan();

        foreach (var c in hash) {
            if (validChars.IndexOf(c) == -1) return false;
        }

        return true;
    }
}