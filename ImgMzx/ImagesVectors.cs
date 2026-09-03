namespace ImgMzx;

public partial class Images : IDisposable
{
    private float[] _vectors = [];
    private readonly Dictionary<string, int> _hashToIndex = [];
    private Stack<int> _freeSlots = new();

    // Per-slot metadata mirrored from the database so that image selection never
    // has to issue a query. Kept in sync by AddVector/RemoveVector and by the
    // single choke point UpdateImgInDatabase().
    private string[] _slotToHash = [];
    private long[] _lastViewTicks = [];
    private int[] _historyLength = [];
    private int[] _rate = [];

    private int AddVector(string hash, ReadOnlySpan<float> vector, long lastViewTicks, int historyLength, int rate)
    {
        lock (_lock) {
            var slot = _freeSlots.Pop();
            vector.CopyTo(_vectors.AsSpan(slot * AppConsts.VectorSize, AppConsts.VectorSize));
            _hashToIndex[hash] = slot;
            _slotToHash[slot] = hash;
            _lastViewTicks[slot] = lastViewTicks;
            _historyLength[slot] = historyLength;
            _rate[slot] = rate;
            return slot;
        }
    }

    private void RemoveVector(string hash)
    {
        lock (_lock) {
            if (_hashToIndex.TryGetValue(hash, out int slot)) {
                _hashToIndex.Remove(hash);
                _slotToHash[slot] = string.Empty;
                _lastViewTicks[slot] = 0;
                _historyLength[slot] = 0;
                _rate[slot] = 0;
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
