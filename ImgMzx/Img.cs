using SixLabors.ImageSharp.Processing;

namespace ImgMzx
{
    public class Img
    {
        public string Hash { get; }
        public string Name { get; }
        public float[] Vector { get; }
        public float[] Faces { get; }
        public RotateMode RotateMode { get; }
        public FlipMode FlipMode { get; }
        public DateTime LastView { get; }
        public string Family { get; }
        public bool Verified { get; }
        public string Horizon { get; }
        public int Counter { get; }
        public string Nodes { get; }

        public Img(
            string hash,
            string name,
            float[] vector,
            float[] faces,
            RotateMode rotatemode,
            FlipMode flipmode,
            DateTime lastview,
            string family,
            bool verified,
            string horizon,
            int counter,
            string nodes
            )
        {
            Hash = hash;
            Name = name;
            Vector = vector;
            Faces = faces;
            RotateMode = rotatemode;
            FlipMode = flipmode;
            LastView = lastview;
            Family = family;
            Verified = verified;
            Horizon = horizon;
            Counter = counter;
            Nodes = nodes;
        }
    }
}