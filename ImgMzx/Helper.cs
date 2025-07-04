using System.IO;

namespace ImgMzx
{
    public static class Helper
    {
        public static string TimeIntervalToString(TimeSpan ts)
        {
            if (ts.TotalDays >= 2.0)
                return $"{ts.TotalDays:F0} days";

            if (ts.TotalDays >= 1.0)
                return $"{ts.TotalDays:F0} day";

            if (ts.TotalHours >= 2.0)
                return $"{ts.TotalHours:F0} hours";

            if (ts.TotalHours >= 1.0)
                return $"{ts.TotalHours:F0} hour";

            if (ts.TotalMinutes >= 2.0)
                return $"{ts.TotalMinutes:F0} minutes";

            if (ts.TotalMinutes >= 1.0)
                return $"{ts.TotalMinutes:F0} minute";

            return $"{ts.TotalSeconds:F0} seconds";
        }

        public static string SizeToString(long size)
        {
            var str = $"{size} b";
            if (size < 1024)
                return str;

            var kSize = (double)size / 1024;
            str = $"{kSize:F1} Kb";
            if (kSize < 1024) {
                return str;
            }

            kSize /= 1024;
            str = $"{kSize:F4} Mb";
            return str;
        }

        public static void CleanupDirectories(string startLocation, IProgress<string> progress)
        {
            foreach (var directory in Directory.GetDirectories(startLocation)) {
                CleanupDirectories(directory, progress);
                if (Directory.GetFiles(directory).Length != 0 || Directory.GetDirectories(directory).Length != 0) {
                    continue;
                }

                progress.Report($"{directory} deleting{AppConsts.CharEllipsis}");
                try {
                    Directory.Delete(directory, false);
                }
                catch (IOException) {
                }
            }
        }

        public static string GetHashDistance(string hash, float distance)
        {
            var rounded = (int)Math.Round(distance * 10000);
            rounded = Math.Max(0, Math.Min(9999, rounded));
            var radius = $"{rounded:D4}{hash}";
            return radius;
        }

        public static byte[] ArrayFromFloat(float[] array)
        {
            var buffer = new byte[array.Length * sizeof(float)];
            Buffer.BlockCopy(array, 0, buffer, 0, buffer.Length);
            return buffer;
        }

        public static float[] ArrayToFloat(byte[] buffer)
        {
            var array = new float[buffer.Length / sizeof(float)];
            Buffer.BlockCopy(buffer, 0, array, 0, buffer.Length);
            return array;
        }
        public static byte[] ArrayFromInt(int[] array)
        {
            var buffer = new byte[array.Length * sizeof(int)];
            Buffer.BlockCopy(array, 0, buffer, 0, buffer.Length);
            return buffer;
        }

        public static int[] ArrayToInt(byte[] buffer)
        {
            var array = new int[buffer.Length / sizeof(int)];
            Buffer.BlockCopy(buffer, 0, array, 0, buffer.Length);
            return array;
        }
    }
}