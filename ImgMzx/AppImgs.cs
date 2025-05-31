using Microsoft.Data.Sqlite;
using System.Data;
using System.Text;
using SixLabors.ImageSharp.Processing;
using System.Windows;
using System.Diagnostics;
using System.CodeDom;
using System.Security.Policy;

namespace ImgMzx;

public static class AppImgs
{
    private static readonly object _lock = new();
    private static SqliteConnection _sqlConnection = new();
    private static SortedList<string, Img> _imgList = new(); // hash/img
    private static SortedList<string, string> _nameList = new(); // name/hash
    private static readonly List<float[]> _recent = new();

    public static string Status { get; private set; } = string.Empty;

    public static void Load(string filedatabase, IProgress<string>? progress, out int maxImages)
    {
        AppDatabase.Load(filedatabase, progress, out SortedList<string, Img> imgList, out SortedList<string, string> nameList, out maxImages);
        lock (_lock) {
            _imgList = imgList;
            _nameList = nameList;
        }
    }

    public static void Add(Img img)
    {
        lock (_lock) {
            _imgList.Add(img.Hash, img);
            _nameList.Add(img.Name, img.Hash);
        }
    }

    public static int Count()
    {
        int count;
        lock (_lock) {
            if (_imgList.Count != _nameList.Count) {
                throw new Exception();
            }

            count = _imgList.Count;
        }

        return count;
    }

    private static bool ContainsKey(string key)
    {
        bool result;
        lock (_lock) {
            result = key.Length >= 32 ? _imgList.ContainsKey(key) : _nameList.ContainsKey(key);
        }

        return result;
    }

    public static string GetName(string hash)
    {
        string name;
        var length = 5;
        do {
            length++;
            name = hash[..length].ToLower();
        } while (ContainsKey(name));

        return name;
    }

    public static bool TryGet(string hash, out Img? img)
    {
        lock (_lock) {
            return _imgList.TryGetValue(hash, out img);
        }
    }

    public static bool TryGetByName(string name, out Img? img)
    {
        img = null;
        lock (_lock) {
            return _nameList.TryGetValue(name, out var hash) && TryGet(hash, out img);
        }
    }

    public static void Delete(string key)
    {
        lock (_lock) {
            if (key.Length >= 32) {
                if (TryGet(key, out var img)) {
                    _imgList.Remove(key);
                    if (img != null) {
                        _nameList.Remove(img.Name);
                        AppDatabase.Delete(key);
                    }
                }

                foreach (var e in _imgList.Values) {
                    if (e.IsInHistory(img!.Name)) {
                        e.RemoveFromHistory(img.Name);
                    }
                }
            }
            else {
                if (TryGetByName(key, out var img)) {
                    if (img != null) {
                        _imgList.Remove(img.Hash);
                        AppDatabase.Delete(img.Hash);
                    }

                    _nameList.Remove(key);
                    foreach (var e in _imgList.Values) {
                        if (e.IsInHistory(key)) {
                            e.RemoveFromHistory(key);
                        }
                    }
                }
            }
        }
    }

    public static Img GetForCheck()
    {
        lock (_lock) {
            foreach (var img in _imgList.Values) {
                if (img.Hash.Equals(img.Next) || !_imgList.ContainsKey(img.Next)) {
                    return img;
                }
            }

            return _imgList
                .MinBy(e => e.Value.LastCheck)
                .Value;
        }
    }

    public static Tuple<Img, float>[] GetBeam(Img img)
    {
        List<Img> shadow = new();
        lock (_lock) {
            foreach (var e in _imgList.Values) {
                if (img.Hash.Equals(e.Hash) || img.IsInHistory(e.Name)) {
                    continue;
                }

                shadow.Add(e);
            }
        }

        var distances = new float[shadow.Count];
        var vx = img.GetVector();
        Parallel.For(0, distances.Length, i => { distances[i] = AppVit.GetDistance(vx, shadow[i].GetVector()); });

        var beam = shadow
            .Zip(distances, Tuple.Create)
            .OrderBy(t => t.Item2)
            .ToArray();

        return beam;
    }

    public static DateTime GetMinimalLastView()
    {
        lock (_lock) {
            return _imgList.Min(e => e.Value.LastView).AddSeconds(-1);
        }
    }

    public static Img? GetForView()
    {
        Img? imgX = null;

        lock (_lock) {
            foreach (var img in _imgList.Values) {
                if (img.Hash.Equals(img.Next)) {
                    continue;
                }

                if (!_imgList.TryGetValue(img.Next, out Img? imgY)) {
                    continue;
                }

                if (imgY == null) {
                    continue;
                }

                if (imgX == null) {
                    imgX = img;
                    continue;
                }

                if (img.Score < imgX.Score) {
                    imgX = img;
                    continue;
                }

                if (img.Score > imgX.Score) {
                    continue;
                }

                if (img.Hash.CompareTo(imgX.Hash) < 0) {
                    imgX = img;
                }
            }
        }

        /*
        var pr = new List<Img>();

        lock (_lock) {
            foreach (var img in _imgList.Values) {
                if (img.Hash.Equals(img.Next) || !_imgList.ContainsKey(img.Next)) {
                    continue;
                }

                if (pr.Count == 0) { 
                    pr.Add(img);
                    continue;
                }

                if (img.Score < pr[0].Score) { 
                    pr.Clear();
                    pr.Add(img);
                    continue;
                }

                if (img.Score > pr[0].Score) {
                    continue;
                }

                pr.Add(img);
            }

            imgX = pr.MinBy(e => AppVars.RandomNext(100000));
        }
        */

        var sb = new StringBuilder();
        lock (_lock) {
            var diff = _imgList.Count - AppVars.MaxImages;
            sb.Append($"{_imgList.Count} ({diff})");
        }

        Status = sb.ToString();
        return imgX;

        /*
        var list = new SortedList<int, List<Img>>[2];
        lock (_lock) {
            foreach (var img in _imgList.Values) {
                if (!_imgList.TryGetValue(img.Next, out var imgnext)) {
                    continue;
                }

                var dimg = (int)Math.Round(DateTime.Now.Subtract(img.LastView).TotalDays);
                var dnext = (int)Math.Round(DateTime.Now.Subtract(imgnext.LastView).TotalDays);
                var d = Math.Min(dimg, dnext);
                var v = img.Verified ? 1 : 0;
                if (list[v] == null) {
                    list[v] = new SortedList<int, List<Img>>();
                }

                if (list[v].TryGetValue(d, out var e)) {
                    e.Add(img);
                }
                else {
                    list[v].Add(d, new List<Img> { img });
                }
            }
        }

        var rv = AppVars.RandomNext(10) > 0 ? 0 : 1;
        if (list[rv] == null) {
            rv = 1 - rv;
        }

        var lrv = list[rv].ToList();
        lrv.Reverse();

        var sb = new StringBuilder();
        var gmax = Math.Min(3, list[rv].Count);
        for (var i = 0; i < gmax; i++) {
            if (i > 0) {
                sb.Append("/");
            }

            var e = lrv[i];

            sb.Append(rv > 0 ? "" : "n");
            sb.Append(e.Key);
            sb.Append(':');
            sb.Append(e.Value.Count);
        }

        sb.Append($"/{AppConsts.CharEllipsis}/");
        lock (_lock) {
            var diff = _imgList.Count - AppVars.MaxImages;
            sb.Append($"{_imgList.Count} ({diff})");
        }
        */

        /*
        Status = sb.ToString();

        List<Img> g;
        if (lrv.Count == 1) {
            g = lrv[0].Value;
        }
        else {
            var rnd = AppVars.RandomNext(lrv.Count - 1);
            g = lrv[rnd].Value;
        }

        //var r = AppVars.RandomNext(g.Count);
        //var imgX = g[r];
        var imgX = g.MaxBy(e => e.LastCheck);
        return imgX;
        */

        /*
        if (_recent.Count == 0) {
            _recent.Add(list[list.Count - 1].Item1.GetVector());
        }

        var amax = Math.Min(2000, list.Count);
        var distances = new float[amax, _recent.Count];
        Parallel.For(0, amax, i => {
            var img = list[i];
            Parallel.For(0, _recent.Count, j => { distances[i, j] = AppVit.GetDistance(list[i].Item1.GetVector(), _recent[j]); });
        });

        var imgX = list[0].Item1;
        var maxDistance = 0f;
        for (var i = 0; i < amax; i++) {
            var minDistance = distances[i, 0];
            for (var j = 1; j < _recent.Count; j++) {
                if (distances[i, j] < minDistance) {
                    minDistance = distances[i, j];
                }
            }
             if (minDistance > maxDistance) {
                maxDistance = minDistance;
                imgX = list[i].Item1;
            }
        }

        _recent.Add(imgX.GetVector());
        while (_recent.Count > 200) {
            _recent.RemoveAt(0);
        }
        */
        /*
        list = list
            .OrderBy(e => e.Verified)
            .ThenBy(e => e.Score)
            .Take(10000)
            .ToList();

        var w = new double[list.Count];
        var wsum = 0.0;
        var maxlv = list.Max(e => e.LastView.Ticks / (double)TimeSpan.TicksPerDay);
        for (var i = 0; i < list.Count; i++) {
            var diff = maxlv - (list[i].LastView.Ticks / (double)TimeSpan.TicksPerDay);
            w[i] = diff * diff;
            wsum += w[i];
        }

        for (var i = 0; i < list.Count; i++) {
            w[i] /= wsum;
        }

        var random = AppVars.RandomDouble();
        var a = 0;
        for (a = 0; a < list.Count; a++) {
            if (random < w[a]) {
                break;
            }

            random -= w[a];
        }

        sb.Append($" a:{a}");
        Status = sb.ToString();

        var imgX = list[a];
        */

        /*
        Img[] recent;
        lock (_lock) {
            recent = _imgList.Values.OrderByDescending(e => e.LastView).Take(256).ToArray();
        }
        */

        /*
        var minVerified = list.Min(e => e.Verified);
        list = list.Where(e => e.Verified == minVerified).ToList();
        var minScore = list.Min(e => e.Score);
        list = list.Where(e => e.Score == minScore).ToList();
        */

        /*
        list = list
            .OrderBy(e => e.Verified)
            .ThenBy(e => e.Score)
            .Take(10000)
            .ToList();
        var distances = new float[list.Count, recent.Length];
        Parallel.For(0, list.Count, i => {
            var img = list[i];
            Parallel.For(0, recent.Length, j => { distances[i, j] = AppVit.GetDistance(img.Vector, recent[j].Vector); });
        });

        var imgX = list[0];
        var maxDistance = 0f;
        for (var i = 0; i < list.Count; i++) {
            var minDistance = distances[i, 0];
            for (var j = 1; j < recent.Length; j++) {
                if (distances[i, j] < minDistance) {
                    minDistance = distances[i, j];
                }
            }
            if (minDistance > maxDistance) {
                maxDistance = minDistance;
                imgX = list[i];
            }
        }
        */


        /*
        var minVerified = list.Min(x => x.Verified); 
        list = list.Where(x => x.Verified == minVerified).ToList();
        var minScore = list.Min(x => x.Score);
        list = list.Where(x => x.Score == minScore).ToList();

        var imgX = list.MinBy(e => e.LastView.AddMinutes(AppVars.GetRandomIndex(60*24*7)));
        */
        /*
        var distances = new float[list.Count];
        Parallel.For(0, distances.Length, i => { distances[i] = AppVit.GetDistance(_lastVector, list[i].Vector); });

        // find i where distances[i] is minimal

        var minIndex = 0;
        var minDistance = distances[0];
        for (var i = 1; i < distances.Length; i++) {
            if (distances[i] < minDistance) {
                minDistance = distances[i];
                minIndex = i;
            }
        }

        var imgX = list[minIndex];
        Array.Copy(list[minIndex].Vector, _lastVector, list[minIndex].Vector.Length);
        */

        /*
        var imgX = list[0];
        for (var a = 0; a < 10000; a++) {
            var randomIndex = AppVars.GetRandomIndex(list.Count);
            var img = list[randomIndex];
            if (imgX.Verified && !img.Verified) {
                imgX = img;
            }
            else if (!imgX.Verified && img.Verified) {
                continue;
            }
            else if (imgX.Score > img.Score) {
                imgX = img;
            }
            else if (imgX.Score < img.Score) {
                continue;
            }
            else if (imgX.LastView > img.LastView) {
                imgX = img;
            }
        }
        */
    }
}