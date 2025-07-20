using System.Security.Cryptography;

namespace ImgMzx
{
    public static class AppVars
    {
        public static Progress<string>? Progress { get; set; }
        public static ManualResetEvent? SuspendEvent { get; set; }
        public static bool ShowXOR { get; set; }
        public static bool ImportRequested { get; set; }
        public static int MaxImages { get; set; }
    }
}