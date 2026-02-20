namespace ImgMzx;

public partial class Images : IDisposable
{
    private float[] _vectors = [];
    private readonly Dictionary<string, int> _hashToIndex = [];
    private Stack<int> _freeSlots = new();

    private int AddVector(string hash, ReadOnlySpan<float> vector)
    {
        lock (_lock) { 
            var slot = _freeSlots.Pop();
            _hashToIndex[hash] = slot;
            vector.CopyTo(_vectors.AsSpan(slot * AppConsts.VectorSize, AppConsts.VectorSize));
            _hashToIndex[hash] = slot;
            return slot;
        }
    }

    private void RemoveVector(string hash)
    {
        lock (_lock) {
            if (_hashToIndex.TryGetValue(hash, out int slot)) {
                _hashToIndex.Remove(hash);
                _freeSlots.Push(slot);
            }
        }
    }

    public void UpdateVector(string hash, ReadOnlySpan<float> vector)
    {
        lock (_lock) {
            if (_hashToIndex.TryGetValue(hash, out int slot)) {
                vector.CopyTo(_vectors.AsSpan(slot * AppConsts.VectorSize, AppConsts.VectorSize));
            }
        }
    }

    public ReadOnlySpan<float> GetVector(string hash)
    {
        lock (_lock) {
            if (_hashToIndex.TryGetValue(hash, out int slot)) {
                return _vectors.AsSpan(slot * AppConsts.VectorSize, AppConsts.VectorSize);
            }

            return [];
        }
    }
}
