using SixLabors.ImageSharp.Processing;

namespace ImgMzx
{
    public class Img
    {
        public string Hash { get; }
        public string Name { get; }
        public float[] Vector { get; }
        public RotateMode RotateMode { get; }
        public FlipMode FlipMode { get; }
        public DateTime LastView { get; }
        public bool Verified { get; }
        public string Next { get; }
        public float Distance { get; }
        public int Score { get; }
        public DateTime LastCheck { get; }

        public float VectorHash { get; }

        public Img(
            string hash,
            string name,
            float[] vector,
            RotateMode rotatemode,
            FlipMode flipmode,
            DateTime lastview,
            bool verified,
            string next,
            float distance,
            int score,
            DateTime lastcheck
            )
        {
            Hash = hash;
            Name = name;
            Vector = vector;
            RotateMode = rotatemode;
            FlipMode = flipmode;
            LastView = lastview;
            Verified = verified;
            Next = next;
            Distance = distance;
            Score = score;
            LastCheck = lastcheck;

            VectorHash = AppVit.GetDeviation(Vector);
        }
    }
}