﻿using Microsoft.Data.Sqlite;
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
    private static SortedList<string, List<string>> _pairList = new(); // name/hash

    public static string Status { get; private set; } = string.Empty;

    public static void Load(string filedatabase, IProgress<string>? progress, out int maxImages)
    {
        AppDatabase.Load(
            filedatabase, 
            progress, 
            out SortedList<string, Img> imgList, 
            out SortedList<string, string> nameList, 
            out SortedList<string, List<string>> pairList,
            out maxImages);
        lock (_lock) {
            _imgList = imgList;
            _nameList = nameList;
            _pairList = pairList;
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
                        var familySize = GetFamilySize(img.Family);
                        if (familySize == 0) {
                            AppDatabase.DeletePair(img.Family);
                        }
                    }
                }
            }
            else {
                if (TryGetByName(key, out var img)) {
                    if (img != null) {
                        _imgList.Remove(img.Hash);
                        AppDatabase.Delete(img.Hash);
                        var familySize = GetFamilySize(img.Family);
                        if (familySize == 0) {
                            AppDatabase.DeletePair(img.Family);
                        }
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

    public static int GetFamilySize(string family)
    {
        lock (_lock) {
            return _imgList.Count(e => e.Value.Family.Equals(family));
        }
    }

    public static List<string> GetFamily(string family)
    {
        lock (_lock) {
            return _imgList.Where(e => e.Value.Family.Equals(family)).Select(e => e.Value.Name).ToList();
        }
    }

    public static void MoveFamily(string s, string d)
    {
        lock (_lock) {
            foreach (var img in _imgList.Values) {
                if (img.Family.Equals(s)) {
                    img.SetFamily(d);
                }
            }
        }
    }

    public static Img? GetX(IProgress<string>? progress)
    {
        lock (_lock) {
            var bins = new SortedList<int, List<Img>>();
            foreach (var img in _imgList.Values) {
                var age = (int)Math.Round(DateTime.Now.Subtract(img.LastView).TotalDays);
                if (age > 0) {
                    if (bins.TryGetValue(age, out var list)) {
                        list.Add(img);
                    }
                    else {
                        bins.Add(age, new List<Img>() { img });
                    }
                }
            }
            
            var rank = new List<Tuple<int, int>>();
            foreach (var e in bins.Keys) {
                var agerandom = e + AppVars.RandomNext(365);
                rank.Add(Tuple.Create(e, agerandom));
            }

            var orderedRank = rank.OrderByDescending(x => x.Item2).ToList();
            var agewinner = orderedRank[0].Item1;
            var r = AppVars.RandomNext(bins[agewinner].Count);
            var imgX = bins[agewinner][r];           
            return imgX;
        }
    }

    public static string? GetInTheSameFamily(string hash)
    {
        lock (_lock) {
            if (!TryGet(hash, out var img)) {
                return null;
            }

            if (img == null) {
                return null;
            }

            var family = GetFamily(img.Family);
            Img? imgX = null;
            foreach (var name in family) {
                if (name.Equals(img.Name)) {
                    continue;
                }
                if (!TryGetByName(name, out Img? imgN)) {
                    continue;
                }

                if (imgN == null) {
                    continue;
                }

                if (imgX == null || imgN.LastView < imgX.LastView) {
                    imgX = imgN;
                }
            }

            if (imgX == null) {
                return null;
            }

            return imgX.Hash;
        }
    }

    public static void UpdateNext(Img imgX, IProgress<string>? progress)
    {
        var pairs = GetPairs(imgX.Family);
        var beam = AppImgs.GetBeam(imgX);
        var index = 0;
        while (
            beam[index].Item1.Hash.Equals(imgX.Hash) || 
            beam[index].Item1.Family.Equals(imgX.Family) ||
            pairs.Contains(beam[index].Item1.Family)) {
            index++;
        }

        var next = beam[index].Item1.Hash;
        var distance = beam[index].Item2;
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

    private static void AddPairInternal(string familyX, string familyY)
    {
        lock (_lock) { 
            if (_pairList.TryGetValue(familyX, out var edges)) {
                if (!edges.Contains(familyY)) {
                    edges.Add(familyY);
                    while (edges.Count > AppConsts.MaxPairs) {
                        edges.RemoveAt(0);
                    }

                    AppDatabase.UpdatePair(familyX, edges);
                }
            }
            else {
                edges = new List<string>() { familyY };
                _pairList.Add(familyX, edges);
                AppDatabase.AddPair(familyX, edges);
            }
        }
    }

    public static void AddPair(string familyX, string familyY)
    {
        AddPairInternal(familyX, familyY);
        AddPairInternal(familyY, familyX);
    }

    public static string[] GetPairs(string family)
    {
        lock (_lock) {
            if (_pairList.TryGetValue(family, out var edges)) {
                return edges.ToArray();
            }
            else {
                return Array.Empty<string>();
            }
        }
    }
}