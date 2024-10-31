using FaceAiSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImgMzx;

public static class AppFace
{
    private static readonly IFaceDetectorWithLandmarks _detector = FaceAiSharpBundleFactory.CreateFaceDetectorWithLandmarks();
    private static readonly IFaceEmbeddingsGenerator _recognizer = FaceAiSharpBundleFactory.CreateFaceEmbeddingsGenerator();

    public static float[] GetVector(Image<Rgb24> image)
    {
        var vectors = new List<float[]>();
        var faces = _detector.DetectFaces(image);
        if (faces.Count > 0) {
            foreach (var face in faces) {
                var imgface = image.Clone();
                _recognizer.AlignFaceUsingLandmarks(imgface, face.Landmarks!);
                var embedding = _recognizer.GenerateEmbedding(imgface);
                vectors.Add(embedding);
            }
        }

        var vector = new float[vectors.Count * 512];
        for (var i = 0; i < vectors.Count; i++) {
            var offset = i * vectors[i].Length;
            for (var j = 0; j < vectors[i].Length; j++) {
                vector[offset + j] = vectors[i][j];
            }
        }

        return vector;
    }

    public static float GetDistance(float[] x, float[] y)
    {
        var distance = 1.1f;
        for (var i = 0; i < x.Length; i += 512) {
            for (var j = 0; j < y.Length; j += 512) {
                var d = 0f;
                for (var k = 0; k < 512; k++) {
                    d += x[i + k] * y[j + k];
                }

                d = 1f - d;
                if (d < distance) {
                    distance = d;
                }
            }
        }

        return distance;
    }
}