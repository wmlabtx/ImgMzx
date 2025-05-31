using System.Security.Cryptography;

namespace ImgMzx
{
    public static class AppVars
    {
        public static Progress<string>? Progress { get; set; }
        public static ManualResetEvent? SuspendEvent { get; set; }
        public static bool ShowXOR { get; set; }
        public static bool ImportRequested { get; set; }

        private static readonly RandomNumberGenerator _random = RandomNumberGenerator.Create();
        public static int RandomNext(int maxValue)
        {
            int result;
            if (Monitor.TryEnter(_random, AppConsts.LockTimeout)) {
                try {
                    var buffer = new byte[8];
                    _random.GetBytes(buffer);
                    var rulong = BitConverter.ToUInt64(buffer, 0);
                    result = (int)(rulong % (ulong)maxValue);
                }
                finally { 
                    Monitor.Exit(_random); 
                }
            }
            else {
                throw new Exception();
            }

            return result;
        }

        public static double RandomDouble()
        {
            double result;
            if (Monitor.TryEnter(_random, AppConsts.LockTimeout)) {
                try {
                    var buffer = new byte[8];
                    _random.GetBytes(buffer);
                    var rulong = BitConverter.ToUInt64(buffer, 0);
                    result = rulong / (double)ulong.MaxValue;
                }
                finally {
                    Monitor.Exit(_random);
                }
            }
            else {
                throw new Exception();
            }

            return result;
        }

        public static byte[] RandomBuffer(int length)
        {
            byte[] buffer;
            if (Monitor.TryEnter(_random, AppConsts.LockTimeout)) {
                try {
                    buffer = new byte[length];
                    _random.GetBytes(buffer);
                }
                finally {
                    Monitor.Exit(_random);
                }
            }
            else {
                throw new Exception();
            }

            return buffer;
        }

        public static int MaxImages { get; set; }
    }
}