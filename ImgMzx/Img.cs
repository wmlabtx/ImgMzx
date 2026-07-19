using SixLabors.ImageSharp.Processing;
using System.Text;

namespace ImgMzx;

public struct Img(
    string hash,
    DateTime lastView,
    RotateMode rotateMode,
    FlipMode flipMode,
    string history,
    Images images)
{
    private readonly Images _images = images;
    private readonly string _hash = hash;
    public string Hash {
        get { return _hash; }
    }

    private DateTime _lastView = lastView;
    public DateTime LastView
    {
        get { return _lastView; }
        set
        {
            _lastView = value;
            _images.UpdateImgInDatabase(_hash, AppConsts.AttributeLastView, value.Ticks);
        }
    }

    private RotateMode _rotateMode = rotateMode;
    public RotateMode RotateMode {
        get { return _rotateMode; }
        set
        {
            _rotateMode = value;
            _images.UpdateImgInDatabase(_hash, AppConsts.AttributeRotateMode, (int)value);
        }
    }

    private FlipMode _flipMode = flipMode;
    public FlipMode FlipMode {
        get { return _flipMode; }
        set
        {
            _flipMode = value;
            _images.UpdateImgInDatabase(_hash, AppConsts.AttributeFlipMode, (int)value);
        }
    }

    private string _history = history;
    public string History {
        get { return _history; }
        set
        {
            _history = value;
            _images.UpdateImgInDatabase(_hash, AppConsts.AttributeHistory, value);
        }
    }

    public SortedSet<string> FromHistory()
    {
        var set = new SortedSet<string>();
        if (!string.IsNullOrEmpty(_history)) {
            for (var offset = 0; offset < _history.Length; offset += AppConsts.HashLength) {
                var hash = _history.Substring(offset, AppConsts.HashLength);
                set.Add(hash);
            }
        }

        return set;
    }

    public void ToHistory(SortedSet<string> history)
    {
        var sb = new StringBuilder();
        foreach (var hash in history) {
            sb.Append(hash);
        }

        History = sb.ToString();
    }

    public readonly ReadOnlySpan<float> Vector {
        get { return _images.GetVector(_hash); }
        set {
            _images.UpdateVector(_hash, value);
            _images.UpdateVectorInDatabase(_hash, value);
        }
    }
}