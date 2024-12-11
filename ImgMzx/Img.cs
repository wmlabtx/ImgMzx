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
        public string History { get; }
        public string Next { get; }
        public int Score { get; }

        public Img(
            string hash,
            string name,
            float[] vector,
            RotateMode rotatemode,
            FlipMode flipmode,
            DateTime lastview,
            bool verified,
            string history,
            string next,
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
            History = history;
            Next = next;
            Score = score;
        }
    }
}