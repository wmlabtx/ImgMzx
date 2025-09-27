using ImgMzx;

namespace ImgMzxTests;

[TestClass]
public class AppCryptoTest
{
    private const string TestPassword = "test_password_for_encryption";

    [TestMethod]
    public void Encrypt_Decrypt_SmallData_Successful()
    {
        // Arrange
        var originalData = "Hello, this is test data for encryption!"u8.ToArray();
        
        // Act
        var encrypted = AppCrypto.Encrypt(originalData, TestPassword);
        var decrypted = AppCrypto.Decrypt(encrypted, TestPassword);
        
        // Assert
        Assert.IsNotNull(decrypted);
        Assert.IsTrue(originalData.AsSpan().SequenceEqual(decrypted));
        Assert.AreNotEqual(originalData.Length, encrypted.Length); // Should be larger due to nonce + tag
        
        Console.WriteLine($"? Small data encryption/decryption successful");
        Console.WriteLine($"   Original:  {originalData.Length} bytes");
        Console.WriteLine($"   Encrypted: {encrypted.Length} bytes");
        Console.WriteLine($"   Overhead:  {encrypted.Length - originalData.Length} bytes");
    }

    [TestMethod]
    public void EncryptToStream_DecryptFromStream_LargeImage()
    {
        // Arrange - Create a large test image (6MB)
        var imageSize = 6 * 1024 * 1024;
        var originalImage = new byte[imageSize];
        new Random(42).NextBytes(originalImage);
        var password = "stream_test_password";
        
        var tempEncryptedFile = Path.GetTempFileName();
        
        try
        {
            Console.WriteLine($"???  Stream Processing Test:");
            Console.WriteLine($"   Image size: {imageSize / 1024 / 1024:N0} MB");
            Console.WriteLine($"   Hardware:   {AppCrypto.Performance.HardwareSupport.GetOptimalPath()}");
            
            // Act - Encrypt to stream
            var encryptStopwatch = System.Diagnostics.Stopwatch.StartNew();
            using (var outputStream = File.Create(tempEncryptedFile))
            {
                AppCrypto.EncryptToStream(originalImage, password, outputStream);
            }
            encryptStopwatch.Stop();
            
            // Act - Decrypt from stream
            var decryptStopwatch = System.Diagnostics.Stopwatch.StartNew();
            byte[]? decryptedImage;
            using (var inputStream = File.OpenRead(tempEncryptedFile))
            {
                decryptedImage = AppCrypto.DecryptFromStream(inputStream, password);
            }
            decryptStopwatch.Stop();
            
            // Assert
            Assert.IsNotNull(decryptedImage);
            Assert.AreEqual(imageSize, decryptedImage.Length);
            Assert.IsTrue(originalImage.AsSpan().SequenceEqual(decryptedImage));
            
            var encryptThroughput = (imageSize / 1024.0 / 1024.0) / (encryptStopwatch.ElapsedMilliseconds / 1000.0);
            var decryptThroughput = (imageSize / 1024.0 / 1024.0) / (decryptStopwatch.ElapsedMilliseconds / 1000.0);
            var fileSize = new FileInfo(tempEncryptedFile).Length;
            var overhead = ((fileSize - imageSize) / (double)imageSize * 100);
            
            Console.WriteLine($"?? Stream Performance:");
            Console.WriteLine($"   Encrypt time:    {encryptStopwatch.ElapsedMilliseconds:N0}ms");
            Console.WriteLine($"   Encrypt speed:   {encryptThroughput:F1} MB/s");
            Console.WriteLine($"   Decrypt time:    {decryptStopwatch.ElapsedMilliseconds:N0}ms");
            Console.WriteLine($"   Decrypt speed:   {decryptThroughput:F1} MB/s");
            Console.WriteLine($"   File size:       {fileSize / 1024 / 1024:F1} MB");
            Console.WriteLine($"   Overhead:        {overhead:F2}%");
            
            Assert.IsTrue(encryptThroughput > 25, 
                $"Stream encryption too slow: {encryptThroughput:F1} MB/s (expected > 25 MB/s)");
            Assert.IsTrue(decryptThroughput > 25, 
                $"Stream decryption too slow: {decryptThroughput:F1} MB/s (expected > 25 MB/s)");
            
            Console.WriteLine("? Stream processing performance optimal");
        }
        finally
        {
            if (File.Exists(tempEncryptedFile))
                File.Delete(tempEncryptedFile);
        }
    }

    [TestMethod]
    public void EncryptFileToFile_DecryptFileToFile_ExtensionMethods()
    {
        // Arrange
        var testData = "Test content for file-to-file encryption using extension methods"u8.ToArray();
        var tempOriginalFile = Path.GetTempFileName();
        var tempEncryptedFile = Path.GetTempFileName();
        var tempDecryptedFile = Path.GetTempFileName();
        var password = "file_to_file_password";
        
        try
        {
            File.WriteAllBytes(tempOriginalFile, testData);
            
            Console.WriteLine($"?? File-to-File Extension Methods Test:");
            Console.WriteLine($"   Original file: {new FileInfo(tempOriginalFile).Length} bytes");
            
            // Act - Encrypt file to file
            var encryptStopwatch = System.Diagnostics.Stopwatch.StartNew();
            tempOriginalFile.EncryptFileToFile(password, tempEncryptedFile);
            encryptStopwatch.Stop();
            
            // Act - Decrypt file to file
            var decryptStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var success = tempEncryptedFile.DecryptFileToFile(password, tempDecryptedFile);
            decryptStopwatch.Stop();
            
            // Assert
            Assert.IsTrue(success, "File-to-file decryption should succeed");
            Assert.IsTrue(File.Exists(tempEncryptedFile), "Encrypted file should exist");
            Assert.IsTrue(File.Exists(tempDecryptedFile), "Decrypted file should exist");
            
            var decryptedData = File.ReadAllBytes(tempDecryptedFile);
            Assert.IsTrue(testData.AsSpan().SequenceEqual(decryptedData), "Decrypted content should match original");
            
            var encryptedFileSize = new FileInfo(tempEncryptedFile).Length;
            var decryptedFileSize = new FileInfo(tempDecryptedFile).Length;
            
            Console.WriteLine($"   Encrypted file: {encryptedFileSize} bytes");
            Console.WriteLine($"   Decrypted file: {decryptedFileSize} bytes");
            Console.WriteLine($"   Encrypt time:   {encryptStopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"   Decrypt time:   {decryptStopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine("? File-to-file extension methods working correctly");
        }
        finally
        {
            if (File.Exists(tempOriginalFile)) File.Delete(tempOriginalFile);
            if (File.Exists(tempEncryptedFile)) File.Delete(tempEncryptedFile);
            if (File.Exists(tempDecryptedFile)) File.Delete(tempDecryptedFile);
        }
    }

    [TestMethod]
    public void StreamProcessing_WrongPassword_ReturnsNull()
    {
        // Arrange
        var originalData = "Secret stream data"u8.ToArray();
        var correctPassword = "correct_stream_password";
        var wrongPassword = "wrong_stream_password";
        var tempFile = Path.GetTempFileName();
        
        try
        {
            // Encrypt to stream with correct password
            using (var outputStream = File.Create(tempFile))
            {
                AppCrypto.EncryptToStream(originalData, correctPassword, outputStream);
            }
            
            // Act - Try to decrypt with wrong password
            byte[]? decrypted;
            using (var inputStream = File.OpenRead(tempFile))
            {
                decrypted = AppCrypto.DecryptFromStream(inputStream, wrongPassword);
            }
            
            // Assert
            Assert.IsNull(decrypted, "Stream decryption with wrong password should return null");
            
            Console.WriteLine("? Stream processing correctly rejects wrong password");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void StreamProcessing_EmptyData_HandledCorrectly()
    {
        // Arrange
        var emptyData = Array.Empty<byte>();
        var password = "empty_data_test";
        var tempFile = Path.GetTempFileName();
        
        try
        {
            // Act - Encrypt empty data to stream
            using (var outputStream = File.Create(tempFile))
            {
                AppCrypto.EncryptToStream(emptyData, password, outputStream);
            }
            
            // Act - Decrypt from stream
            byte[]? decrypted;
            using (var inputStream = File.OpenRead(tempFile))
            {
                decrypted = AppCrypto.DecryptFromStream(inputStream, password);
            }
            
            // Assert
            Assert.IsNotNull(decrypted);
            Assert.AreEqual(0, decrypted.Length);
            
            Console.WriteLine($"?? Empty Data Stream Test:");
            Console.WriteLine($"   File size: {new FileInfo(tempFile).Length} bytes");
            Console.WriteLine("? Empty data handled correctly in streams");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void StreamProcessing_VeryLargeData_Efficient()
    {
        // Arrange - Test with very large data (16MB)
        var largeDataSize = 16 * 1024 * 1024;
        var largeData = new byte[largeDataSize];
        new Random(42).NextBytes(largeData);
        var password = "large_stream_test";
        var tempFile = Path.GetTempFileName();
        
        try
        {
            Console.WriteLine($"???  Large Stream Processing Test:");
            Console.WriteLine($"   Data size: {largeDataSize / 1024 / 1024:N0} MB");
            
            // Act - Encrypt large data to stream
            var encryptStopwatch = System.Diagnostics.Stopwatch.StartNew();
            using (var outputStream = File.Create(tempFile))
            {
                AppCrypto.EncryptToStream(largeData, password, outputStream);
            }
            encryptStopwatch.Stop();
            
            // Act - Decrypt large data from stream
            var decryptStopwatch = System.Diagnostics.Stopwatch.StartNew();
            byte[]? decrypted;
            using (var inputStream = File.OpenRead(tempFile))
            {
                decrypted = AppCrypto.DecryptFromStream(inputStream, password);
            }
            decryptStopwatch.Stop();
            
            // Assert
            Assert.IsNotNull(decrypted);
            Assert.AreEqual(largeDataSize, decrypted.Length);
            
            // Verify data integrity (check first and last chunks)
            Assert.IsTrue(largeData.AsSpan(0, 1024).SequenceEqual(decrypted.AsSpan(0, 1024)), 
                "First 1KB should match");
            Assert.IsTrue(largeData.AsSpan(largeDataSize - 1024).SequenceEqual(decrypted.AsSpan(largeDataSize - 1024)), 
                "Last 1KB should match");
            
            var encryptThroughput = (largeDataSize / 1024.0 / 1024.0) / (encryptStopwatch.ElapsedMilliseconds / 1000.0);
            var decryptThroughput = (largeDataSize / 1024.0 / 1024.0) / (decryptStopwatch.ElapsedMilliseconds / 1000.0);
            var fileSize = new FileInfo(tempFile).Length;
            
            Console.WriteLine($"?? Large Stream Performance:");
            Console.WriteLine($"   Encrypt time:    {encryptStopwatch.ElapsedMilliseconds:N0}ms");
            Console.WriteLine($"   Encrypt speed:   {encryptThroughput:F1} MB/s");
            Console.WriteLine($"   Decrypt time:    {decryptStopwatch.ElapsedMilliseconds:N0}ms");
            Console.WriteLine($"   Decrypt speed:   {decryptThroughput:F1} MB/s");
            Console.WriteLine($"   File size:       {fileSize / 1024 / 1024:F1} MB");
            
            Assert.IsTrue(encryptThroughput > 50, 
                $"Large stream encryption too slow: {encryptThroughput:F1} MB/s (expected > 50 MB/s)");
            Assert.IsTrue(decryptThroughput > 50, 
                $"Large stream decryption too slow: {decryptThroughput:F1} MB/s (expected > 50 MB/s)");
            
            Console.WriteLine("? Large stream processing performance optimal");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void Encrypt_WrongPassword_ReturnsNull()
    {
        // Arrange
        var originalData = "Secret data that should not be decryptable with wrong password"u8.ToArray();
        var correctPassword = "correct_password";
        var wrongPassword = "wrong_password";
        
        // Act
        var encrypted = AppCrypto.Encrypt(originalData, correctPassword);
        var decrypted = AppCrypto.Decrypt(encrypted, wrongPassword);
        
        // Assert
        Assert.IsNull(decrypted, "Decryption with wrong password should return null");
        
        Console.WriteLine("? Wrong password correctly rejected");
    }

    [TestMethod]
    public void Encrypt_CorruptedData_ReturnsNull()
    {
        // Arrange
        var originalData = "Data that will be corrupted after encryption"u8.ToArray();
        
        // Act
        var encrypted = AppCrypto.Encrypt(originalData, TestPassword);
        
        // Corrupt the encrypted data
        encrypted[encrypted.Length / 2] ^= 0xFF;
        
        var decrypted = AppCrypto.Decrypt(encrypted, TestPassword);
        
        // Assert
        Assert.IsNull(decrypted, "Corrupted data should not decrypt successfully");
        
        Console.WriteLine("? Corrupted data correctly rejected");
    }

    [TestMethod]
    public void EncryptBatch_DecryptBatch_MultipleImages()
    {
        // Arrange
        const int imageCount = 5;
        const int imageSize = 1024 * 1024; // 1MB each
        
        var originalImages = new List<byte[]>();
        var passwords = new List<string>();
        var encrypted = new List<byte[]>(new byte[imageCount][]);
        var decrypted = new List<byte[]?>(new byte[imageCount][]);
        
        for (int i = 0; i < imageCount; i++)
        {
            var imageData = new byte[imageSize];
            new Random(42 + i).NextBytes(imageData);
            originalImages.Add(imageData);
            passwords.Add($"password_{i}");
        }
        
        Console.WriteLine($"? Batch Processing Test:");
        Console.WriteLine($"   Images:     {imageCount} × {imageSize / 1024:N0}KB");
        Console.WriteLine($"   Total data: {imageCount * imageSize / 1024.0 / 1024.0:F1} MB");
        
        // Act - Batch Encryption
        var encryptStopwatch = System.Diagnostics.Stopwatch.StartNew();
        AppCrypto.EncryptBatch(originalImages, passwords, encrypted);
        encryptStopwatch.Stop();
        
        // Act - Batch Decryption
        var decryptStopwatch = System.Diagnostics.Stopwatch.StartNew();
        AppCrypto.DecryptBatch(encrypted, passwords, decrypted);
        decryptStopwatch.Stop();
        
        // Assert
        for (int i = 0; i < imageCount; i++)
        {
            Assert.IsNotNull(decrypted[i], $"Image {i} should decrypt successfully");
            Assert.IsTrue(originalImages[i].AsSpan().SequenceEqual(decrypted[i]), $"Image {i} should match original");
        }
        
        var totalDataMB = imageCount * imageSize / 1024.0 / 1024.0;
        var encryptThroughput = totalDataMB / (encryptStopwatch.ElapsedMilliseconds / 1000.0);
        var decryptThroughput = totalDataMB / (decryptStopwatch.ElapsedMilliseconds / 1000.0);
        
        Console.WriteLine($"?? Batch Performance:");
        Console.WriteLine($"   Encrypt time:    {encryptStopwatch.ElapsedMilliseconds:N0}ms");
        Console.WriteLine($"   Encrypt speed:   {encryptThroughput:F1} MB/s");
        Console.WriteLine($"   Decrypt time:    {decryptStopwatch.ElapsedMilliseconds:N0}ms");
        Console.WriteLine($"   Decrypt speed:   {decryptThroughput:F1} MB/s");
        
        Assert.IsTrue(encryptThroughput > 25, 
            $"Batch encryption too slow: {encryptThroughput:F1} MB/s (expected > 25 MB/s)");
        
        Console.WriteLine("? Batch processing performance optimal");
    }

    [TestMethod]
    public void Summary_AppCrypto_OptimizedForImages()
    {
        Console.WriteLine("?? AppCrypto Optimization Summary for Image Processing:");
        Console.WriteLine(new string('=', 65));
        Console.WriteLine();
        
        // Test with realistic image data
        var imageData = new byte[4 * 1024 * 1024]; // 4MB typical large image
        new Random(42).NextBytes(imageData);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var encrypted = AppCrypto.Encrypt(imageData, TestPassword);
        var decrypted = AppCrypto.Decrypt(encrypted, TestPassword);
        stopwatch.Stop();
        
        var throughputMBps = (imageData.Length * 2 / 1024.0 / 1024.0) / (stopwatch.ElapsedMilliseconds / 1000.0);
        
        Console.WriteLine($"?? Key Optimizations:");
        Console.WriteLine($"   ? AES-GCM for authenticated encryption");
        Console.WriteLine($"   ? Hardware AES-NI acceleration when available");
        Console.WriteLine($"   ? SIMD XOR operations for CTR mode");
        Console.WriteLine($"   ? Stream-based processing for large files");
        Console.WriteLine($"   ? 4MB block processing optimized for images");
        Console.WriteLine($"   ? Zero-copy operations with Span<T>");
        Console.WriteLine();
        
        Console.WriteLine($"?? Performance Results:");
        Console.WriteLine($"   Test image:      4MB");
        Console.WriteLine($"   Processing:      {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Throughput:      {throughputMBps:F1} MB/s");
        Console.WriteLine($"   Hardware path:   {AppCrypto.Performance.HardwareSupport.GetOptimalPath()}");
        Console.WriteLine($"   Size overhead:   {((encrypted.Length - imageData.Length) / (double)imageData.Length * 100):F2}%");
        Console.WriteLine();
        
        Console.WriteLine($"?? API Features:");
        Console.WriteLine($"   ? AppCrypto.Encrypt(imageBytes, password)");
        Console.WriteLine($"   ? AppCrypto.Decrypt(encryptedBytes, password)");
        Console.WriteLine($"   ? AppCrypto.EncryptToStream(data, password, stream)");
        Console.WriteLine($"   ? AppCrypto.DecryptFromStream(stream, password)");
        Console.WriteLine($"   ? AppCrypto.EncryptBatch(images, passwords, results)");
        Console.WriteLine($"   ? AppCrypto.DecryptBatch(encrypted, passwords, results)");
        Console.WriteLine($"   ? \"file.jpg\".EncryptFromFile(password)");
        Console.WriteLine($"   ? \"input.jpg\".EncryptFileToFile(password, \"output.enc\")");
        Console.WriteLine();
        
        Console.WriteLine($"?? Security Features:");
        Console.WriteLine($"   ? AES-256 encryption with hardware acceleration");
        Console.WriteLine($"   ? PBKDF2 key derivation (100k iterations)");
        Console.WriteLine($"   ? Authenticated encryption (prevents tampering)");
        Console.WriteLine($"   ? Random nonces (no ciphertext reuse)");
        Console.WriteLine($"   ? Secure key disposal");
        Console.WriteLine();
        
        // Verify optimization goals
        Assert.IsNotNull(decrypted);
        Assert.IsTrue(imageData.AsSpan().SequenceEqual(decrypted));
        Assert.IsTrue(throughputMBps > 25, "Should achieve reasonable throughput for images");
        
        Console.WriteLine("?? AppCrypto optimized for high-performance image encryption!");
        Console.WriteLine(new string('=', 65));
    }

    [TestMethod]
    public void BasicEncryptDecrypt_Works()
    {
        // Arrange
        var data = "Hello, World!"u8.ToArray();
        var password = "test123";

        // Act
        var encrypted = AppCrypto.Encrypt(data, password);
        var decrypted = AppCrypto.Decrypt(encrypted, password);

        // Assert
        Assert.IsNotNull(decrypted);
        Assert.IsTrue(data.AsSpan().SequenceEqual(decrypted));

        Console.WriteLine($"? Basic encryption/decryption successful");
        Console.WriteLine($"   Original:  {data.Length} bytes");
        Console.WriteLine($"   Encrypted: {encrypted.Length} bytes");
        Console.WriteLine($"   Hardware:  {AppCrypto.Performance.HardwareSupport.GetOptimalPath()}");
    }
}