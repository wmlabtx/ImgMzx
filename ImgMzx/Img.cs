using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace ImgMzx
{
    public class Img
    {
        public string Hash { get; }
        public string Name { get; }

        public DateTime LastView { get; private set; }
        public DateTime LastCheck { get; private set; }
        public RotateMode RotateMode { get; private set; }
        public FlipMode FlipMode { get; private set; }
        public bool Verified { get; private set; }
        public int Score { get; private set; }
        public string Next { get; private set; }
        public float Distance { get; private set; }
        public string Family { get; private set; }

        private float[] _vector;

        public Img(
            string hash,
            string name,
            float[] vector,
            DateTime lastview,
            RotateMode rotatemode = RotateMode.None,
            FlipMode flipmode = FlipMode.None,
            bool verified = false,
            string next = "",
            float distance = float.MaxValue,
            int score = 0,
            DateTime lastcheck = default,
            string family = ""
            )
        {
            Hash = hash;
            Name = name;
            RotateMode = rotatemode;
            FlipMode = flipmode;
            LastView = lastview;
            Verified = verified;
            Next = next;
            Distance = distance;
            Score = score;
            LastCheck = lastcheck;
            Family = string.IsNullOrEmpty(family) ? name : family;

            _vector = vector;
        }

        public float[] GetVector()
        {
            return _vector;
        }

        public long GetRawLastCheck()
        {
            return LastCheck.Ticks;
        }

        public long GetRawLastView()
        {
            return LastView.Ticks;
        }

        public byte[] GetRawVector()
        {
            return Helper.ArrayFromFloat(_vector);
        }

        public void UpdateLastView()
        {
            LastView = DateTime.Now;
            AppDatabase.ImgUpdateProperty(Hash, AppConsts.AttributeLastView, GetRawLastView());
        }

        public void UpdateLastCheck()
        {
            LastCheck = DateTime.Now;
            AppDatabase.ImgUpdateProperty(Hash, AppConsts.AttributeLastCheck, GetRawLastCheck());
        }

        public void SetVector(float[] vector)
        {
            _vector = vector;
            AppDatabase.ImgUpdateProperty(Hash, AppConsts.AttributeVector, GetRawVector());
        }

        public void SetRotateMode(RotateMode rotateMode)
        {
            RotateMode = rotateMode;
            AppDatabase.ImgUpdateProperty(Hash, AppConsts.AttributeRotateMode, (int)rotateMode);
        }

        public void SetFlipMode(FlipMode flipMode)
        {
            FlipMode = flipMode;
            AppDatabase.ImgUpdateProperty(Hash, AppConsts.AttributeFlipMode, (int)flipMode);
        }

        public void UpdateVerified()
        {
            Verified = true;
            AppDatabase.ImgUpdateProperty(Hash, AppConsts.AttributeVerified, Verified);
        }

        public void SetScore(int score)
        {
            Score = score;
            AppDatabase.ImgUpdateProperty(Hash, AppConsts.AttributeScore, score);
        }

        public void SetNext(string next)
        {
            Next = next;
            AppDatabase.ImgUpdateProperty(Hash, AppConsts.AttributeNext, next);
        }

        public void SetDisnance(float distance)
        {
            Distance = distance;
            AppDatabase.ImgUpdateProperty(Hash, AppConsts.AttributeDistance, distance);
        }

        public void SetFamily(string family)
        {
            Family = family;
            AppDatabase.ImgUpdateProperty(Hash, AppConsts.AttributeFamily, family);
        }
    }
}