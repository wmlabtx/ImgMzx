using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Policy;
using System.Text;

namespace ImgMzx;

public partial class Images(string filedatabase, string filevit, string filemask) : IDisposable
{
    private readonly Lock _lock = new();
    private bool disposedValue;

    private ConcurrentDictionary<string, Img> _imgs = new();
    private readonly Database _database = new(filedatabase);
    private readonly Vit _vit = new(filevit, filemask);
    private readonly Panel?[] _imgPanels = { null, null };

    public bool ShowXOR;

    private int _maxImages;

    public void Load(IProgress<string>? progress) { 
        (_imgs, _maxImages) = _database.Load(progress);
    }

    public int GetCount()
    {
        lock (_lock) {
            return _imgs.Count;
        }
    }

    public bool ContainsImg(string hash)
    {
        lock (_lock) {
            return _imgs.ContainsKey(hash);
        }
    }

    public Img GetImg(string hash)
    {
        lock (_lock) {
            return _imgs[hash];
        }
    }

    public void UpdateImg(Img img)
    {
        lock (_lock) {
            _imgs[img.Hash] = img;
            _database.UpdateImgInDatabase(img);
        }
    }

    public void DeleteImg(string hash)
    {
        lock (_lock) {
            _imgs.Remove(hash, out _);
            _database.DeleteImgInDatabase(hash);
            AppFile.DeleteMex(hash, DateTime.Now);
        }
    }  

    public string GetHashLastCheck()
    {
        lock (_lock) {
            foreach (var img in _imgs.Values) {
                if (!_imgs.ContainsKey(img.Next)) {
                    return img.Hash;
                }
            }

            return _imgs.Values.OrderBy(img => img.LastCheck).First().Hash;
        }
    }

    public string? GetX()
    {
        lock (_lock) {
            var minlv = _imgs.Min(img => img.Value.LastView);
            var lv = minlv.AddDays(365);
            var s = _imgs.Values.Where(img => img.LastView <= lv).ToArray();
            var rindex = Random.Shared.Next(s.Length);
            return s[rindex].Hash;
        }
    }

    public DateTime GetLastView()
    {
        lock (_lock) {
            return _imgs.Min(img => img.Value.LastView);
        }
    }

    public Panel? GetPanel(int id)
    {
        return (id == 0 || id == 1) ? _imgPanels[id] : null;
    }

    private string GetNearGroup()
    {
        lock (_lock) {
            var minLen = _imgs.Min(img => img.Value.History.Length) / AppConsts.HashLength;
            var minScore = _imgs
                .Where(img => img.Value.History.Length == minLen * AppConsts.HashLength)
                .Min(img => img.Value.Score);
            var groupLen = _imgs
                .Where(img => img.Value.History.Length == minLen * AppConsts.HashLength && img.Value.Score == minScore)
                .Count();
            return $"{minLen}:{minScore}:{groupLen}";
        }
    }

    public (Img img, float Distance)[] GetBeam(ReadOnlySpan<float> query)
    {
        lock (_lock) {
            var results = new (Img img, float Distance)[_imgs.Count];
            var localquery = query.ToArray();
            var localarray = _imgs.Values.ToArray();
            Parallel.For(0, _imgs.Count, i => {
                var img = localarray[i];
                var vector = img.Vector;
                var distance = Vit.ComputeDistance(localquery, vector);
                results[i] = (img, distance);
            });

            Array.Sort(results, (a, b) => a.Distance.CompareTo(b.Distance));
            return results;
        }
    }

    public string GetNext(string hash, string? hashD = null)
    {
        var sb = new StringBuilder();
        var totalimages = GetCount();
        var nearGroup = GetNearGroup();
        var diff = totalimages - _maxImages;
        sb.Append($"{nearGroup}/{totalimages} ({diff}) ");

        if (!ContainsImg(hash)) {
            return "image not found";
        }

        var img = GetImg(hash);
        if (img.Vector.Length != AppConsts.VectorSize) {
            var imagedata = AppFile.ReadMex(hash);
            if (imagedata != null) {
                using var image = AppBitmap.GetImage(imagedata);
                if (image != null) {
                    var vector = _vit.CalculateVector(image);
                    if (vector != null) {
                        img.Vector = vector;
                        UpdateImg(img);
                    }
                }
            }
        }
        
        var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(img.LastCheck));
        sb.Append($"[{lastcheck} ago] ");

        var hs = Helper.HistoryFromString(img.History);
        var hsnew = new HashSet<string>(StringComparer.Ordinal);
        foreach (var h in hs) {
            if (ContainsImg(h)) {
                hsnew.Add(h);
            }
        }

        if (hs.Count != hsnew.Count) {
            img.History = Helper.HistoryToString(hsnew);
        }

        var oldNext = img.Next;
        if (string.IsNullOrEmpty(oldNext)) {
            oldNext = "XXXX";
        }

        if (!string.IsNullOrEmpty(hashD)) {
            DeleteImg(hashD);
        }

        lock (_lock) {
            var next = oldNext;
            var distance = 1f;
            var beam = GetBeam(img.Vector);
            for (var i = 0; i < beam.Length; i++) {
                if (beam[i].img.Hash.Equals(hash)) {
                    continue;
                }

                if (hsnew.Contains(beam[i].img.Hash)) {
                    continue;
                }

                next = beam[i].img.Hash;
                distance = beam[i].Distance;
                i++;
                break;
            }

            var middle = beam.Length / 2;
            var proximity = beam
                .Where(e => !e.img.Hash.Equals(hash))
                .OrderByDescending(e => e.img.LastView)
                .Take(middle)
                .Select(e => e.Distance)
                .Min();

            if (string.IsNullOrEmpty(next)) {
                return "no suitable next image found";
            }

            if (
                !oldNext.Equals(next) ||
                Math.Abs(img.Distance - distance) >= 0.0001f) {
                
                if (!oldNext.Equals(next)) {
                    img.Next = next;
                }

                if (Math.Abs(img.Distance - distance) >= 0.0001f) {
                    sb.Append($"{img.Distance:F4} {AppConsts.CharRightArrow} {distance:F4} ");
                    img.Distance = distance;
                }
            }

            img.LastCheck = DateTime.Now;
            UpdateImg(img);
            return sb.ToString();
        }
    }

    public void Find(string? hashX, IProgress<string>? progress)
    {
        for (var i = 0; i < 100; i++) {
            var hashToCheck = GetHashLastCheck();
            if (hashToCheck == null) {
                 progress?.Report("nothing to show");
                return;
            }

            var imgToCheck = GetImg(hashToCheck);
            var message = GetNext(hashToCheck);
            if (string.IsNullOrEmpty(message)) {
                continue;
            }

            if (string.IsNullOrEmpty(message)) {
                DeleteImg(hashToCheck);
                continue;
            }

            if (message.Contains(AppConsts.CharRightArrow)) {
                progress?.Report(message);
            }
        }

        do {
            if (string.IsNullOrEmpty(hashX)) {
                hashX = GetX();
                if (string.IsNullOrEmpty(hashX)) {
                    var totalcount = GetCount();
                    progress?.Report($"totalcount = {totalcount}");
                    return;
                }
            }

            if (!SetLeftPanel(hashX)) {
                DeleteImg(hashX);
                hashX = null;
                continue;
            }

            var imgX = GetImg(hashX);
            if (imgX.Vector.Length != AppConsts.VectorSize) {
                progress?.Report($"calculating vector{AppConsts.CharEllipsis}");
                var imagedata = AppFile.ReadMex(hashX);
                if (imagedata != null) {
                    using var image = AppBitmap.GetImage(imagedata);
                    if (image != null) {
                        var vector = _vit.CalculateVector(image);
                        if (vector != null) {
                            imgX.Vector = vector;
                            UpdateImg(imgX);
                        }
                    }
                }
                else {
                    DeleteImg(hashX);
                    hashX = null;
                    continue;
                }
            }

            var hashY = imgX.Next;
            if (!SetRightPanel(hashY)) {
                DeleteImg(hashY);
                hashY = null;
                var message = GetNext(hashX);
                progress?.Report(message);
                imgX = GetImg(hashX);
                hashY = imgX.Next;
                if (!SetRightPanel(hashY)) {
                    hashX = null;
                    continue;
                }
            }

            var imgY = GetImg(hashY);
            var sb = new StringBuilder();
            var totalimages = GetCount();
            var nearGroup = GetNearGroup();
            var diff = totalimages - _maxImages;
            sb.Append($"{nearGroup}/{totalimages} ({diff}) ");
            var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(imgX.LastCheck));
            sb.Append($"[{lastcheck} ago] ");
            sb.Append($"{imgX.Distance:F4} ");
            progress?.Report(sb.ToString());
            break;
        }
        while (true);
    }

    public bool UpdateRightPanel()
    {
        return SetRightPanel(_imgPanels[1]!.Value.Hash);
    }

    public void Confirm(IProgress<string>? progress)
    {
        var hashX = _imgPanels[0]!.Value.Hash;
        var imgX = GetImg(hashX);
        var hashY = _imgPanels[1]!.Value.Hash;
        var imgY = GetImg(hashY);

        progress?.Report($"Calculating{AppConsts.CharEllipsis}");

        imgX.Score = imgX.Score + 1;
        imgX.LastView = DateTime.Now;
        var hs = Helper.HistoryFromString(imgX.History);
        hs.Add(hashY);
        imgX.History = Helper.HistoryToString(hs);
        UpdateImg(imgX);
        var message = GetNext(hashX);
        progress?.Report(message);

        imgY.Score = imgY.Score + 1;
        imgY.LastView = DateTime.Now;
        hs = Helper.HistoryFromString(imgY.History);
        hs.Add(hashX);
        imgY.History = Helper.HistoryToString(hs);
        UpdateImg(imgY);
        message = GetNext(hashY);
        progress?.Report(message);
    }

    public void DeleteLeft(IProgress<string>? progress)
    {
        progress?.Report($"Calculating{AppConsts.CharEllipsis}");
        var hashX = _imgPanels[0]!.Value.Hash;
        var hashY = _imgPanels[1]!.Value.Hash;

        DeleteImg(hashX);

        var imgY = GetImg(hashY);
        imgY.Score = imgY.Score + 1;
        imgY.LastView = DateTime.Now;
        UpdateImg(imgY);
        var message = GetNext(hashY, hashX);
        progress?.Report(message);
    }

    public void DeleteRight(IProgress<string>? progress)
    {
        progress?.Report($"Calculating{AppConsts.CharEllipsis}");
        var hashX = _imgPanels[0]!.Value.Hash;
        var hashY = _imgPanels[1]!.Value.Hash;

        DeleteImg(hashY);


        var imgX = GetImg(hashX);
        imgX.Score = imgX.Score + 1;
        imgX.LastView = DateTime.Now;
        UpdateImg(imgX);
        var message = GetNext(hashX, hashY);
        progress?.Report(message);
    }

    public static string Export(string hashE)
    {
        var imagedata = AppFile.ReadMex(hashE);
        if (imagedata != null) {
            var ext = AppBitmap.GetExtension(imagedata);
            var recycledName = AppFile.GetRecycledName(hashE, ext, AppConsts.PathExport, DateTime.Now);
            AppFile.CreateDirectory(recycledName);
            File.WriteAllBytes(recycledName, imagedata);
            var name = Path.GetFileName(recycledName);
            return name;
        }

        return string.Empty;
    }

    public void Export(IProgress<string>? progress)
    {
        progress?.Report($"Exporting{AppConsts.CharEllipsis}");
        var filename0 = Export(_imgPanels[0]!.Value.Hash);
        var filename1 = Export(_imgPanels[1]!.Value.Hash);
        progress?.Report($"Exported to {filename0} and {filename1}");
    }

    public void Rotate(string hash, RotateMode rotatemode, FlipMode flipmode)
    {
        var imagedata = AppFile.ReadMex(hash);
        if (imagedata == null) {
            return;
        }

        using var image = AppBitmap.GetImage(imagedata, rotatemode, flipmode);
        if (image == null) {
            return;
        }

        var rvector = _vit.CalculateVector(image);
        var img = GetImg(hash);
        img.Vector = rvector;
        img.RotateMode = rotatemode;
        img.FlipMode = flipmode;
        UpdateImg(img);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue) {
            if (disposing) {
                lock (_lock) {
                    if (_imgPanels[0]?.Image != null) {
                        _imgPanels[0]?.Image.Dispose();
                    }

                    if (_imgPanels[1]?.Image != null) {
                        _imgPanels[1]?.Image.Dispose();
                    }
                }
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}