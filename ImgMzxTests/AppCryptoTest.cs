using ImgMzx;

namespace ImgMzxTests;

[TestClass]
public class AppCryptoTest
{
    private const string TestPassword = "test_password_for_encryption";

    [TestMethod]
    public void Encrypt_Decrypt_Roundtrip()
    {
        var originalData = "Hello, test data!"u8.ToArray();
        var encrypted = AppCrypto.Encrypt(originalData, TestPassword);
        var decrypted = AppCrypto.Decrypt(encrypted, TestPassword);

        Assert.IsNotNull(decrypted);
        Assert.IsTrue(originalData.AsSpan().SequenceEqual(decrypted));
    }

    [TestMethod]
    public void Decrypt_WrongPassword_ReturnsNull()
    {
        var originalData = "Secret data"u8.ToArray();
        var encrypted = AppCrypto.Encrypt(originalData, TestPassword);
        var decrypted = AppCrypto.Decrypt(encrypted, "wrong_password");

        Assert.IsNull(decrypted);
    }
}