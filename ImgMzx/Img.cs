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
        public string Horizon { get; }
        public int Counter { get; }
        public string Family { get; }
        public int Score { get; }

        public Img(
            string hash,
            string name,
            float[] vector,
            RotateMode rotatemode,
            FlipMode flipmode,
            DateTime lastview,
            bool verified,
            string horizon,
            int counter,
            string family,
            int score
            )
        {
            Hash = hash;
            Name = name;
            Vector = vector;
            RotateMode = rotatemode;
            FlipMode = flipmode;
            LastView = lastview;
            Verified = verified;
            Horizon = horizon;
            Counter = counter;
            Family = family;
            Score = score;
        }
    }
}