namespace ImgMzx;

public partial class Images : IDisposable
{
    private float[] _recent = [];

    public void UpdateRecent(int index, ReadOnlySpan<float> vector)
    {
        lock (_lock) {
            vector.CopyTo(_recent.AsSpan(index * AppConsts.VectorSize, AppConsts.VectorSize));
        }

        UpdateRecentInDatabase(index, vector);
    }

    public ReadOnlySpan<float> GetRecent(int index)
    {
        lock (_lock) {
            return _recent.AsSpan(index * AppConsts.VectorSize, AppConsts.VectorSize);
        }
    }
}
