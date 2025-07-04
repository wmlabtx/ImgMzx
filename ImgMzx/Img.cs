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
        public int Family { get; private set; }

        private float[] _vector;
        private SortedSet<int> _history;

        public Img(
            string hash,
            string name,
            float[] vector,
            DateTime lastview,
            SortedSet<int> history,
            RotateMode rotatemode = RotateMode.None,
            FlipMode flipmode = FlipMode.None,
            bool verified = false,
            string next = "",
            float distance = float.MaxValue,
            int score = 0,
            DateTime lastcheck = default,
            int family = 0
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
            Family = family;

            _vector = vector;

            _history = new SortedSet<int>(history);
        }

        public float[] GetVector()
        {
            return _vector;
        }

        public byte[] GetRawHistory()
        {
            return Helper.ArrayFromInt(_history.ToArray<int>());
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

        public void AddToHistory(int family)
        {
            if (_history.Add(family)) {
                AppDatabase.ImgUpdateProperty(Hash, AppConsts.AttributeHistory, GetRawHistory());
            }
        }

        public void RemoveFromHistory(int family)
        {
            if (_history.Remove(family)) {
                AppDatabase.ImgUpdateProperty(Hash, AppConsts.AttributeHistory, GetRawHistory());
            }
        }

        public bool IsInHistory(int family)
        {
            return _history.Contains(family);
        }

        public int GetHistorySize()
        {
            return _history.Count;
        }

        public SortedSet<int> GetHistory()
        {
            return new SortedSet<int>(_history);
        }

        public void SetFamily(int family)
        {
            Family = family;
            AppDatabase.ImgUpdateProperty(Hash, AppConsts.AttributeFamily, family);
        }
    }
}