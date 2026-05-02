using SixLabors.ImageSharp.Processing;

namespace ImgMzx;

public struct Img(
    string hash,
    DateTime lastView,
    DateTime lastCheck,
    RotateMode rotateMode,
    FlipMode flipMode,
    int score,
    string next,
    float distance,
    int family,
    int flag,
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
            if (_lastView.Ticks > 0 && _lastView.Ticks < _minValidTicks)
                throw new ArgumentException($"LastView too old: {_lastView} (ticks={_lastView.Ticks})");
            _images.UpdateImgInDatabase(_hash, AppConsts.AttributeLastView, value.Ticks);
        }
    }

    private static readonly long _minValidTicks = new DateTime(1970, 1, 1).Ticks;

    private DateTime _lastCheck = lastCheck;
    public DateTime LastCheck
    {
        get { return _lastCheck; }
        set
        {
            _lastCheck = value;
            if (_lastCheck.Ticks > 0 && _lastCheck.Ticks < _minValidTicks)
                throw new ArgumentException($"LastCheck too old: {_lastCheck} (ticks={_lastCheck.Ticks})");
            _images.UpdateImgInDatabase(_hash, AppConsts.AttributeLastCheck, value.Ticks);
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

    private int _score = score;
    public int Score {
        get { return _score; }
        set
        {
            _score = value;
            _images.UpdateImgInDatabase(_hash, AppConsts.AttributeScore, value);
        }
    }

    private string _next = next;
    public string Next {
        get { return _next; }
        set
        {
            _next = value;
            _images.UpdateImgInDatabase(_hash, AppConsts.AttributeNext, value);
        }
    }

    private float _distance = distance;
    public float Distance {
        get { return _distance; }
        set
        {
            _distance = value;
            _images.UpdateImgInDatabase(_hash, AppConsts.AttributeDistance, value);
        }
    }

    private int _family = family;
    public int Family {
        get { return _family; }
        set {
            _family = value;
            _images.UpdateImgInDatabase(_hash, AppConsts.AttributeFamily, value);
        }
    }

    private int _flag = flag;
    public int Flag {
        get { return _flag; }
        set
        {
            _flag = value;
            _images.UpdateImgInDatabase(_hash, AppConsts.AttributeFlag, value);
        }
    }

    public void ResetFlag()
    {
        _flag = 0;
    }

    public readonly ReadOnlySpan<float> Vector {
        get { return _images.GetVector(_hash); }
        set {
            _images.UpdateVector(_hash, value);
            _images.UpdateVectorInDatabase(_hash, value);
        }
    }
}