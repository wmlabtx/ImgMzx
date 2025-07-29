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
                if (img.Name.Equals(img.Next) || !_nameList.ContainsKey(img.Next)) {
                    return img;
                }
            }

            return _imgList
                .MinBy(e => e.Value.LastCheck)
                .Value;
        }
    }

    public static List<Tuple<string, float>> GetBeam(Img img)
    {
        List<Img> shadow;
        lock (_lock) {
            shadow = new List<Img>(_imgList.Values);
            shadow.Remove(img);
        }

        var distances = new float[shadow.Count];
        var vx = img.GetVector();
        Parallel.For(0, distances.Length, i => { distances[i] = AppVit.GetDistance(vx, shadow[i].GetVector()); });

        var beam = new List<Tuple<string, float>>(shadow.Count);
        for (var i = 0; i < shadow.Count; i++) {
            beam.Add(Tuple.Create(shadow[i].Name, distances[i]));
        }

        return beam.OrderBy(e => e.Item2).ToList();
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
            var grouped = _imgList.Values
                .Where(e => !e.Name.Equals(e.Next) && _nameList.ContainsKey(e.Next))
                .Where(e => !string.IsNullOrEmpty(e.Key))
                .GroupBy(e => e.Key)
                .ToDictionary(g => g.Key, g => g.Max(e => e.LastView));
            if (grouped.Count > 0) {
                var key = grouped.MinBy(g => g.Value).Key;
                var imgX = _imgList.Values
                    .Where(e => e.Key.Equals(key))
                    .MinBy(e => e.LastView);
                return imgX;
            }
            else {
                var imgX = _imgList.Values
                    .Where(e => !e.Name.Equals(e.Next) && _nameList.ContainsKey(e.Next))
                    .MinBy(e => e.LastView);
                return imgX;
            }

            /*
            var bins = new SortedList<int, List<Img>>();
            foreach (var img in _imgList.Values) {
                if (img.Name.Equals(img.Next) || !_nameList.ContainsKey(img.Next)) {
                    continue;
                }

                var age = (int)Math.Round(DateTime.Now.Subtract(img.LastView).TotalDays);
                if (bins.TryGetValue(age, out var list)) {
                    list.Add(img);
                }
                else {
                    bins.Add(age, new List<Img>() { img });
                }
            }

            if (bins.Count == 0) {
                return null;
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
            */
        }
    }

    public static string? GetYName(Img imgX, IProgress<string>? progress)
    {
        var sb = new StringBuilder();
        lock (_lock) {
            var diff = _imgList.Count - AppVars.MaxImages;
            sb.Append($"{_imgList.Count} ({diff})");
        }

        sb.Append($" {imgX.Distance:F4}");
        Status = sb.ToString();
        lock (_lock) {
            var nameY = imgX.Next;
            var imgYHash = _nameList[nameY];
            var imgY = _imgList[imgYHash];
            /*
            if (!string.IsNullOrEmpty(imgX.Key) && string.IsNullOrEmpty(imgY.Key)) {
                imgY.SetKey(imgX.Key);
            }
            else if (string.IsNullOrEmpty(imgX.Key) && !string.IsNullOrEmpty(imgY.Key)) {
                imgX.SetKey(imgY.Key);
            }
            */

            return imgY.Name;
        }
    }

    public static string[] GetKeys()
    {
        lock (_lock) {
            return _imgList
                .Where(e => !string.IsNullOrEmpty(e.Value.Key))
                .GroupBy(e => e.Value.Key)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToArray();
        }
    }

    public static string SuggestKey(Img img)
    {
        List<Img> shadow;
        lock (_lock) {
            shadow = new List<Img>(_imgList.Values);
            shadow.Remove(img);
        }

        var keyGroups = shadow
            .Where(e => !string.IsNullOrEmpty(e.Key))
            .GroupBy(e => e.Key)
            .ToDictionary(g => g.Key, g => g.ToList());

        var bestKey = string.Empty;
        var bestDistance = 2f;
        var vx = img.GetVector();
        foreach (var kvp in keyGroups) {
            var key = kvp.Key;
            var group = kvp.Value;
            var groupDistances = new float[group.Count];
            
            Parallel.For(0, groupDistances.Length, i => { groupDistances[i] = AppVit.GetDistance(vx, group[i].GetVector()); });
            var avgDistance = groupDistances.Order().Take(3).Average();
            if (avgDistance < bestDistance) {
                bestDistance = avgDistance;
                bestKey = key;
            }
        }

        return bestKey;
    }
}