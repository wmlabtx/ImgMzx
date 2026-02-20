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
            _images.UpdateImgInDatabase(_hash, AppConsts.AttributeLastView, value);
        }
    }

    private DateTime _lastCheck = lastCheck;
    public DateTime LastCheck
    {
        get { return _lastCheck; }
        set
        {
            _lastCheck = value;
            _images.UpdateImgInDatabase(_hash, AppConsts.AttributeLastCheck, value);
        }
    }

    private RotateMode _rotateMode = rotateMode;
    public RotateMode RotateMode {
        get { return _rotateMode; }
        set
        {
            _rotateMode = value;
            _images.UpdateImgInDatabase(_hash, AppConsts.AttributeRotateMode, value);
        }
    }

    private FlipMode _flipMode = flipMode;
    public FlipMode FlipMode {
        get { return _flipMode; }
        set
        {
            _flipMode = value;
            _images.UpdateImgInDatabase(_hash, AppConsts.AttributeFlipMode, value);
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

    private string _history = history;
    public string History {
        get { return _history; }
        set
        {
            _history = value;
            _images.UpdateImgInDatabase(_hash, AppConsts.AttributeHistory, value);
        }
    }

    public readonly ReadOnlySpan<float> Vector {
        get { return _images.GetVector(_hash); }
        set {
            _images.UpdateVector(_hash, value);
            _images.UpdateVectorInDatabase(_hash, value);
        }
    }
}