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

        private float[] _vector;

        public int MaxIndex { get; private set; }

        public Img(
            string hash,
            string name,
            float[] vector,
            DateTime lastview,
            RotateMode rotatemode,
            FlipMode flipmode,
            bool verified,
            string next,
            float distance,
            int score,
            DateTime lastcheck
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

            _vector = vector;
            MaxIndex = GetMaxVectorIndex();
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
            MaxIndex = GetMaxVectorIndex();
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

        public int GetMaxVectorIndex()
        {
            if (_vector.Length == 0) {
                return -1;
            }
                
            var maxIndex = 0;
            var maxValue = _vector[0];
            for (var i = 1; i < _vector.Length; i++)
            {
                if (_vector[i] > maxValue)
                {
                    maxValue = _vector[i];
                    maxIndex = i;
                }
            }

            return maxIndex;
        }
    }
}