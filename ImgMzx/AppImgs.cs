using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Processing;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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
            beam.Add(Tuple.Create(shadow[i].Hash, distances[i]));
        }

        return beam.OrderBy(e => e.Item2).ToList();
    }

    public static DateTime GetMinimalLastView()
    {
        lock (_lock) {
            return _imgList.Min(e => e.Value.LastView).AddSeconds(-1);
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

            var imgX = _imgList.Values.MinBy(e => e.LastCheck);
            if (imgX == null) {
                throw new Exception("No images found for check.");
            }

            return imgX;
        }
    }

    public static Img GetForView()
    {
        lock (_lock) {
            Img imgX = _imgList.Values.First();
            var lvmin = long.MaxValue;
            foreach (var img in _imgList.Values) {
                if (img.Hash.Equals(img.Next)) {
                    continue;
                }

                if (_imgList.TryGetValue(img.Next, out var imgY)) {
                    var lvmax = Math.Max(img.LastView.Ticks, imgY.LastView.Ticks);
                    if (lvmax < lvmin) {
                        lvmin = lvmax;
                        imgX = img;
                    }
                }
            }

            return imgX;
        }
    }

    public static Img GetX(IProgress<string>? progress)
    {
        var imgX = GetForView();
        return imgX;
    }

    public static Img GetY(Img imgX, IProgress<string>? progress)
    {
        Status = AppImgs.GetStatus();
        if (AppImgs.TryGet(imgX.Next, out var imgY)) {
            if (imgY != null) {
                return imgY;
            }
            else {
                throw new Exception("ImgY not found.");
            }
        }

        return imgX;
        // throw new Exception("ImgY not found.");
    }

    public static string GetStatus()
    {
        var sb = new StringBuilder();
        lock (_lock) {
            var diff = _imgList.Count - AppVars.MaxImages;
            sb.Append($"{_imgList.Count} ({diff})");
        }

        return sb.ToString();
    }

    public static List<Img> GetAllImages()
    {
        lock (_lock) {
            return new List<Img>(_imgList.Values);
        }
    }

    public static List<Img> GetRandomImages(int count)
    {
        lock (_lock) {
            var allImages = _imgList.Values.ToList();
            if (allImages.Count <= count) {
                return new List<Img>(allImages);
            }

            var random = new Random();
            var selected = new List<Img>();
            var indices = new HashSet<int>();

            while (selected.Count < count && indices.Count < allImages.Count) {
                var index = random.Next(allImages.Count);
                if (indices.Add(index)) {
                    selected.Add(allImages[index]);
                }
            }

            return selected;
        }
    }

    public static List<Img> GetImagesOrderedByLastView(int count)
    {
        lock (_lock) {
            return _imgList.Values
                .OrderBy(img => img.LastView)
                .Take(count)
                .ToList();
        }
    }

    public static List<Img> GetImagesOrderedByScore(int count, bool ascending = true)
    {
        lock (_lock) {
            var query = ascending
                ? _imgList.Values.OrderBy(img => img.Score)
                : _imgList.Values.OrderByDescending(img => img.Score);

            return query.Take(count).ToList();
        }
    }

    public static bool IsLoaded()
    {
        lock (_lock) {
            return _imgList.Count > 0;
        }
    }

    /*
    public static int GetNewFamily()
    {
        lock (_lock) {
            var ids = _imgList.Where(e => e.Value.Family > 0).Select(e => e.Value.Family).Distinct().OrderBy(e => e).ToArray();
            if (ids.Length == 0) {
                return 1;
            }

            if (ids.Length == ids.Last()) {
                return ids.Length + 1;
            }

            for (int i = 0; i < ids.Length; i++) {
                if (ids[i] > i + 1) {
                    return i + 1;
                }
            }

            throw new Exception("No family found.");
        }
    }

    public static int GetFamilySize(int family)
    {
        lock (_lock) {
            return _imgList.Count(e => e.Value.Family == family);
        }
    }

    public static List<Img> GetFamily(int family)
    {
        lock (_lock) {
            return _imgList.Values.Where(e => e.Family == family).ToList();
        }
    }

    public static void MoveFamily(int s, int d)
    {
        lock (_lock) {
            foreach (var img in _imgList.Values) {
                if (img.Family == s) {
                    img.SetFamily(d);
                }
            }
        }
    }
    */
}