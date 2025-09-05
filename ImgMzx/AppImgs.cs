using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Processing;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
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
    private static List<string> _nexts = new();

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

    public static SortedList<string, Img> GetShadow()
    {
        lock (_lock) {
            return new SortedList<string, Img>(_imgList);
        }
    }

    public static Img GetImgForCheck()
    {
        var shadow = GetShadow();
        foreach (var e in shadow.Values) {
            if (e.Hash.Equals(e.Next) || !shadow.ContainsKey(e.Next)) {
                return e;
            }
        }

        var imgX = shadow.Values.MinBy(e => e.LastCheck);
        return imgX!;
    }

    public static Img GetImgForView()
    {
        Img? imgX = null;
        lock (_lock) {
            while (_nexts.Count > 0) {
                var hash = _nexts[0];
                _nexts.RemoveAt(0);
                if (_imgList.TryGetValue(hash, out imgX)) {
                    return imgX;
                }
            }
        }

        var shadow = GetShadow();
        var maxAge = 0.0;
        foreach (var img in shadow.Values) {
            if (img.Hash.Equals(img.Next) || !shadow.ContainsKey(img.Next)) {
                continue;
            }

            var age = DateTime.Now.Subtract(img.LastView).TotalDays;
            age += Random.Shared.NextDouble() * 365.0;
            if (imgX == null || img.Score < imgX.Score) {
                maxAge = age;
                imgX = img;
            }
            else if (img.Score == imgX.Score && age > maxAge) {
                maxAge = age;
                imgX = img;
            }
        }

        var beam = GetBeam(imgX!);
        lock (_lock) {
            _nexts.AddRange(beam.Take(1000).Select(e => e.Item1));
        }

        return imgX!;

        /*
        var shadow = GetShadow();
        Img? imgX = null;
        foreach (var img in shadow.Values) {
            if (img.Hash.Equals(img.Next) || !shadow.ContainsKey(img.Next)) {
                continue;
            }

            else if (imgX == null || img.Distance < imgX.Distance) {
                imgX = img;
            }
        }
        

        return imgX!;
        */

        /*
        var shadow = GetShadow();
        Img? imgX = null;
        var maxAge = 0.0;
        foreach (var img in shadow.Values) {
            if (img.Hash.Equals(img.Next) || !shadow.ContainsKey(img.Next)) {
                continue;
            }

            var age = DateTime.Now.Subtract(img.LastView).TotalDays;
            age += Random.Shared.NextDouble() * 365.0;
            if (imgX == null || img.Score < imgX.Score) {
                maxAge = age;
                imgX = img;
            }
            else if (img.Score == imgX.Score && age > maxAge) {
                maxAge = age;
                imgX = img;
            }
        }
        

        return imgX!;
        */
    }

    public static Img GetX(IProgress<string>? progress)
    {
        var imgX = GetImgForView();
        return imgX;
    }

    public static Img GetY(Img imgX, IProgress<string>? progress)
    {
        Status = AppImgs.GetStatus();
        if (AppImgs.TryGet(imgX.Next, out var imgY)) {
            if (imgY != null) {
                var distance = AppVit.GetDistance(imgX.GetVector(), imgY.GetVector());
                Status += $" {distance:F4}";
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
}