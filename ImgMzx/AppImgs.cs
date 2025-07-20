using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Processing;
using System;
using System.CodeDom;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Security.Policy;
using System.Text;
using System.Windows;

namespace ImgMzx;

public static class AppImgs
{
    private static readonly object _lock = new();
    private static SqliteConnection _sqlConnection = new();
    private static SortedList<string, Img> _imgList = new(); // hash/img
    private static SortedList<string, string> _nameList = new(); // name/hash

    public static string Status { get; private set; } = string.Empty;

    public static void Load(string filedatabase, IProgress<string>? progress, out int maxImages)
    {
        AppDatabase.Load(
            filedatabase, 
            progress, 
            out SortedList<string, Img> imgList, 
            out SortedList<string, string> nameList, 
            out maxImages);
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
            }
            else {
                if (TryGetByName(key, out var img)) {
                    if (img != null) {
                        _imgList.Remove(img.Hash);
                        AppDatabase.Delete(img.Hash);
                    }

                    _nameList.Remove(key);
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
        List<Img> shadow;
        lock (_lock) {
            shadow = new List<Img>(_imgList.Values);
        }

        shadow.Remove(img);

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

    public static Img? GetX(IProgress<string>? progress)
    {
        lock (_lock) {
            var bins = new SortedList<int, List<Img>>();
            foreach (var img in _imgList.Values) {
                var age = (int)Math.Round(DateTime.Now.Subtract(img.LastView).TotalDays);
                if (bins.TryGetValue(age, out var list)) {
                    list.Add(img);
                }
                else {
                    bins.Add(age, new List<Img>() { img });
                }
            }

            if (bins.Count == 0) {
                foreach (var img in _imgList.Values) {
                    var age = (int)Math.Round(DateTime.Now.Subtract(img.LastView).TotalDays);
                    if (bins.TryGetValue(age, out var list)) {
                        list.Add(img);
                    }
                    else {
                        bins.Add(age, new List<Img>() { img });
                    }
                }
            }

            var minAge = bins.Keys.Min();
            var maxAge = bins.Keys.Max();
            var midAge = (maxAge - minAge) / 4;

            var rank = new List<Tuple<int, int>>();
            foreach (var e in bins.Keys) {
                var agerandom = e + Random.Shared.Next(midAge);
                rank.Add(Tuple.Create(e, agerandom));
            }

            var orderedRank = rank.OrderByDescending(x => x.Item2).ToList();
            var ageWinner = orderedRank[0].Item1;
            var binWinner = bins[ageWinner];
            var r = Random.Shared.Next(binWinner.Count);
            var imgX = binWinner[r];
            return imgX;
        }
    }

    public static void UpdateNext(Img imgX, IProgress<string>? progress)
    {
        var beam = AppImgs.GetBeam(imgX);
        var minDistance = beam.Min(t => t.Item2);
        var maxDistance = beam.Max(t => t.Item2);
        var midDistance = (maxDistance - minDistance) / 4f;
        var rank = new List<Tuple<Img, float>>();
        foreach (var e in beam) {
            var randomDistance = e.Item2 + (float)(midDistance * Random.Shared.NextDouble());
            rank.Add(Tuple.Create(e.Item1, randomDistance));
        }

        var orderedRank = rank.OrderBy(x => x.Item2).ToList();
        var next = orderedRank[0].Item1.Hash;
        var distance = orderedRank[0].Item2;
        var olddistance = imgX.Distance;
        var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(imgX.LastCheck));
        if (!imgX.Next.Equals(next) || Math.Abs(imgX.Distance - distance) >= 0.0001f) {
            progress!.Report($" [{lastcheck} ago] {olddistance:F4} {AppConsts.CharRightArrow} {distance:F4}");
            imgX.SetNext(next);
            imgX.SetDisnance(distance);
        }

        imgX.UpdateLastCheck();
    }

    public static string? GetY(Img imgX, IProgress<string>? progress)
    {
        var sb = new StringBuilder();
        lock (_lock) {
            var diff = _imgList.Count - AppVars.MaxImages;
            sb.Append($"{_imgList.Count} ({diff})");
        }

        sb.Append($" {imgX.Distance:F4}");
        Status = sb.ToString();
        return imgX.Next;
    }
}