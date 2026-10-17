using System.Numerics.Tensors;

namespace ImgMzx;

public partial class Images : IDisposable
{
    // W/B live in standardized space z = (v - mu) / sigma. For scanning they are folded back
    // into raw-vector space once per fit: w·z + b == wf·v + bf, with wf = w / sigma and
    // bf = b - wf·mu, so every score stays a single dot product on the stored DINOv3 vector.
    private float[] _wf = new float[AppConsts.VectorSize];
    private float _bf;

    /// <summary>
    /// Raw linear score for a vector: w·z + b. Positive means "good", negative means "bad".
    /// No sigmoid here - the sign is all callers need, and it's the same sign as sigmoid(s).
    /// </summary>
    public float GetScore(ReadOnlySpan<float> vector)
    {
        if (vector.Length != AppConsts.VectorSize) {
            return 0f;
        }

        lock (_lock) {
            return TensorPrimitives.Dot(_wf, vector) + _bf;
        }
    }

    /// <summary>
    /// One online logistic-regression step: nudges W/B toward classifying this vector as
    /// kept (label 1) or deleted (label 0). The weight step is normalized by |z|^2 (NLMS),
    /// so it moves this image's own score by ScoreLearningRate * (y - p) - large while the
    /// model disagrees with the operator, fading to nothing once it agrees.
    /// </summary>
    public void FitScore(ReadOnlySpan<float> vector, bool kept)
    {
        if (vector.Length != AppConsts.VectorSize || float.IsNaN(vector[0])) {
            return;
        }

        lock (_lock) {
            var z = new float[AppConsts.VectorSize];
            TensorPrimitives.Subtract(vector, _mu, z);
            TensorPrimitives.Divide(z, _sigma, z);

            var s = TensorPrimitives.Dot(_w, z) + _b;
            var p = 1f / (1f + MathF.Exp(-s));
            var y = kept ? 1f : 0f;
            var error = y - p;

            var norm2 = TensorPrimitives.Dot(z, z);
            if (norm2 <= 0f) {
                return;
            }

            var step = AppConsts.ScoreLearningRate * error / norm2;
            var newW = new float[AppConsts.VectorSize];
            for (var i = 0; i < AppConsts.VectorSize; i++) {
                newW[i] = _w[i] * (1f - AppConsts.ScoreWeightDecay) + step * z[i];
            }

            W = newW;
            B = _b + AppConsts.ScoreBiasLearningRate * error;
            UpdateFoldedScore();
        }
    }

    private void UpdateFoldedScore()
    {
        lock (_lock) {
            TensorPrimitives.Divide(_w, _sigma, _wf);
            _bf = _b - TensorPrimitives.Dot(_wf, _mu);
        }
    }

    /// <summary>
    /// Per-dimension mean and standard deviation over the first 'count' loaded vectors.
    /// Centering removes the large component all DINOv3 CLS vectors share (otherwise every
    /// fit mostly moves W along it, acting as a second bias); scaling evens out the few
    /// high-magnitude dimensions so they don't dominate the step.
    /// </summary>
    private void ComputeScoreSpace(int count)
    {
        lock (_lock) {
            var partitions = Math.Min(Environment.ProcessorCount, Math.Max(1, count));
            var sum = new double[AppConsts.VectorSize];
            var sumSq = new double[AppConsts.VectorSize];
            var n = 0L;
            var sync = new Lock();

            Parallel.For(
                0, partitions,
                () => (Sum: new double[AppConsts.VectorSize], SumSq: new double[AppConsts.VectorSize], N: 0L),
                (partition, _, local) => {
                    var from = (int)((long)count * partition / partitions);
                    var to = (int)((long)count * (partition + 1) / partitions);
                    for (var slot = from; slot < to; slot++) {
                        if (_slotToHash[slot].Length == 0) {
                            continue;
                        }

                        var vector = _vectors.AsSpan(slot * AppConsts.VectorSize, AppConsts.VectorSize);
                        if (float.IsNaN(vector[0])) {
                            continue;
                        }

                        for (var i = 0; i < AppConsts.VectorSize; i++) {
                            local.Sum[i] += vector[i];
                            local.SumSq[i] += (double)vector[i] * vector[i];
                        }

                        local.N++;
                    }

                    return local;
                },
                local => {
                    lock (sync) {
                        for (var i = 0; i < AppConsts.VectorSize; i++) {
                            sum[i] += local.Sum[i];
                            sumSq[i] += local.SumSq[i];
                        }

                        n += local.N;
                    }
                });

            var mu = new float[AppConsts.VectorSize];
            var sigma = new float[AppConsts.VectorSize];
            for (var i = 0; i < AppConsts.VectorSize; i++) {
                if (n == 0) {
                    sigma[i] = 1f;
                    continue;
                }

                var mean = sum[i] / n;
                var variance = Math.Max(0.0, sumSq[i] / n - mean * mean);
                mu[i] = (float)mean;
                sigma[i] = Math.Max((float)Math.Sqrt(variance), AppConsts.ScoreSigmaFloor);
            }

            Mu = mu;
            Sigma = sigma;
        }
    }

    /// <summary>
    /// Picks the hash with the lowest score (most "bad"-leaning) among all in-memory
    /// vectors - a full parallel scan, same shape as FindNearest/UpdateAge, since scores
    /// are cheap (one dot product) and there's no index to keep sorted incrementally.
    /// </summary>
    public string PickNextSubjectByScore()
    {
        lock (_lock) {
            var capacity = _slotToHash.Length;
            var partitions = Math.Min(Environment.ProcessorCount, Math.Max(1, capacity));

            var bestSlot = -1;
            var bestScore = float.MaxValue;
            var sync = new Lock();

            Parallel.For(
                0, partitions,
                () => (Slot: -1, Score: float.MaxValue),
                (partition, _, local) => {
                    var from = (int)((long)capacity * partition / partitions);
                    var to = (int)((long)capacity * (partition + 1) / partitions);
                    for (var slot = from; slot < to; slot++) {
                        if (_slotToHash[slot].Length == 0) {
                            continue;
                        }

                        var vector = _vectors.AsSpan(slot * AppConsts.VectorSize, AppConsts.VectorSize);
                        var score = TensorPrimitives.Dot(_wf, vector) + _bf;
                        if (score < local.Score) {
                            local = (slot, score);
                        }
                    }

                    return local;
                },
                local => {
                    if (local.Slot < 0) {
                        return;
                    }

                    lock (sync) {
                        if (local.Score < bestScore || (local.Score == bestScore && local.Slot < bestSlot)) {
                            bestScore = local.Score;
                            bestSlot = local.Slot;
                        }
                    }
                });

            return bestSlot < 0 ? string.Empty : _slotToHash[bestSlot];
        }
    }
}
