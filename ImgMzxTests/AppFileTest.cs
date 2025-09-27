using ImgMzx;

namespace ImgMzxTests;

/*
[TestClass]
public class AppFileTest
{

    [TestMethod]
    public void GetFilename()
    {
        const string name = "0f3912";
        var subdir = $@"{AppContext.BaseDirectory}subdir";
        var filename = AppFile.GetFileName(name, subdir);
        Assert.AreEqual(filename, $@"{subdir}\0\f\{name}");
    }

    [TestMethod]
    public void Main()
    {
        var basename = "gab_org";
        var data = AppFile.ReadFile($@"{AppContext.BaseDirectory}images\{basename}.jpg");
        Assert.IsNotNull(data);
        var validHash = AppHash.GetHash(data);
        Assert.IsTrue(AppHash.IsValidHash(validHash));
        Assert.AreEqual("chc1i3g5qz1zefq4", validHash);
        var rootpath = $@"{AppContext.BaseDirectory}chunks";
        var backuppath = $@"{AppContext.BaseDirectory}chunks-backup";
        var deletedpath = $@"{AppContext.BaseDirectory}chunks-deleted";

        var mexFile = AppFile.GetFileName(hash: validHash, rootpath: rootpath, extension: AppConsts.MexExtension);
        var bakmexFile = AppFile.GetFileName(hash: validHash, rootpath: backuppath, extension: AppConsts.MexExtension);
        var vecFile = AppFile.GetFileName(hash: validHash, rootpath: rootpath, extension: AppConsts.VecExtension);
        var bakvecFile = AppFile.GetFileName(hash: validHash, rootpath: rootpath, extension: AppConsts.VecExtension);

        if (File.Exists(mexFile)) File.Delete(mexFile);
        if (File.Exists(bakmexFile)) File.Delete(bakmexFile);
        if (File.Exists(vecFile)) File.Delete(vecFile);
        if (File.Exists(bakvecFile)) File.Delete(bakvecFile);

        float[] vector; 
        using (var image = AppBitmap.GetImage(data)) {
            Assert.IsNotNull(image);
            vector = AppVit.GetVector(image);
        }

        AppFile.WriteMex(validHash, data, rootpath, backuppath);
        AppFile.WriteVec(validHash, vector, rootpath, backuppath);

        // Verify file was created
        Assert.IsTrue(File.Exists(mexFile), "MEX file should be created");
        Assert.IsTrue(File.Exists(bakmexFile), "MEX bak file should be created");
        Assert.IsTrue(File.Exists(vecFile), "VEC file should be created");
        Assert.IsTrue(File.Exists(bakvecFile), "VEC bak file should be created");

        // Verify file content can be decrypted
        byte[]? decryptedData = AppFile.ReadMex(validHash, rootpath, backuppath);
        Assert.IsNotNull(decryptedData, "File should be decryptable");
        Assert.IsTrue(data.SequenceEqual(decryptedData), "Decrypted data should match original");

        float[]? readVector = AppFile.ReadVec(validHash, rootpath, backuppath);
        Assert.IsNotNull(readVector, "Vector should be readable");
        Assert.IsTrue(vector.SequenceEqual(readVector), "Read vector should match original");

        // check mex and its backup are identical
        var mainData = File.ReadAllBytes(mexFile);
        var backupData = File.ReadAllBytes(bakmexFile);
        Assert.IsTrue(mainData.SequenceEqual(backupData), "MEX and its backup should be identical");
        mainData = AppCrypto.Decrypt(mainData, validHash);
        backupData = AppCrypto.Decrypt(backupData, validHash);
        Assert.IsNotNull(mainData);
        Assert.IsNotNull(backupData);
        Assert.IsTrue(mainData.SequenceEqual(backupData), "MEX and its backup should be identical");
        var mainHash = AppHash.GetHash(mainData);
        var backupHash = AppHash.GetHash(backupData);
        Assert.AreEqual(mainHash, backupHash, "Hashes of MEX and its backup should match");
        Assert.AreEqual(mainHash, validHash, "Hashes of MEX and original should match");

        // Concurrent access simulation

        var tasks = new Task[5];
        var exceptions = new List<Exception>();
        
        for (int i = 0; i < tasks.Length; i++)
        {
            int taskId = i;
            tasks[i] = Task.Run(() => {
                try
                {
                    var taskData = new byte[data.Length];
                    Array.Copy(data, taskData, data.Length);
                    taskData[^1] = (byte)taskId;
                    
                    AppFile.WriteMex(validHash, taskData, rootpath, backuppath);
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }, CancellationToken.None);
        }
        
        Task.WaitAll(tasks, CancellationToken.None);
        Assert.IsEmpty(exceptions, $"Concurrent access should not cause exceptions: {string.Join(", ", exceptions.Select(e => e.Message))}");
        AppFile.WriteMex(validHash, data, rootpath, backuppath);

        // check if the mex and vec files are corrupted, can we read backup files and restore main files
        File.WriteAllText(mexFile, "corrupted data");
        File.WriteAllText(vecFile, "corrupted data");

        // Verify file content can be decrypted
        decryptedData = AppFile.ReadMex(validHash, rootpath, backuppath);
        Assert.IsNotNull(decryptedData, "File should be decryptable even if it is corrupted");
        Assert.IsTrue(data.SequenceEqual(decryptedData), "Decrypted data should match original");

        readVector = AppFile.ReadVec(validHash, rootpath, backuppath);
        Assert.IsNotNull(readVector, "Vector should be readable even if it is corrupted");
        Assert.IsTrue(vector.SequenceEqual(readVector), "Read vector should match original");

        // Test DeleteMex functionality
        var deleteTime = new DateTime(2024, 12, 15, 14, 30, 45);
        var expectedDeletedFile = AppFile.GetDeletedName(validHash, deleteTime, deletedpath);

        // Clean up any existing deleted file
        if (File.Exists(expectedDeletedFile)) File.Delete(expectedDeletedFile);
        if (Directory.Exists(deletedpath)) Directory.Delete(deletedpath, true);

        // Execute DeleteMex
        AppFile.DeleteMex(validHash, deleteTime, rootpath, backuppath, deletedpath);

        // Verify files are moved/deleted correctly
        Assert.IsFalse(File.Exists(mexFile), "MEX file should be deleted");
        Assert.IsFalse(File.Exists(bakmexFile), "Backup MEX file should be deleted");
        Assert.IsFalse(File.Exists(vecFile), "VEC file should be deleted");
        Assert.IsFalse(File.Exists(bakvecFile), "Backup VEC file should be deleted");
        Assert.IsTrue(File.Exists(expectedDeletedFile), "MEX file should be moved to deleted path");

        // Verify moved file content
        var deletedContent = AppFile.ReadFile(expectedDeletedFile);
        Assert.IsNotNull(deletedContent, "Deleted file should be readable");
        deletedContent = AppCrypto.Decrypt(deletedContent, validHash);
        Assert.IsNotNull(deletedContent, "Deleted file should be decryptable");
        Assert.IsTrue(data.SequenceEqual(deletedContent), "Deleted file content should match original");

        // Cleanup test files
        CleanupTestDirectory(rootpath);
        CleanupTestDirectory(backuppath);
    }

    private static void CleanupTestDirectory(string path)
    {
        try {
            if (Directory.Exists(path)) {
                Directory.Delete(path, true);
            }
        }
        catch {
            // Ignore cleanup errors
        }
    }
}
*/