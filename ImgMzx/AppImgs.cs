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
    private static SortedList<int, DateTime> _idList = new(); // id/lastview

    public static string Status { get; private set; } = string.Empty;

    public static void Load(string filedatabase, IProgress<string>? progress, out int maxImages)
    {
        AppDatabase.Load(
            filedatabase,
            progress,
            out SortedList<string, Img> imgList,
            out SortedList<string, string> nameList,
            out SortedList<int, DateTime> idList,
            out maxImages);
        lock (_lock) {
            _imgList = imgList;
            _nameList = nameList;
            _idList = idList;
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
        var ids = GetIdsForView();
        if (ids == null || ids.Length == 0) {
            throw new Exception("No ids available for view.");
        }

        var shadow = GetShadow();
        for (var i = 0; i < ids.Length; i++) {
            var id = ids[i];
            var scope = shadow.Values.Where(e => e.Id == id).ToArray();
            if (scope.Length == 0) {
                continue;
            }

            Img? imgX = null;
            var maxAge = 0.0;
            foreach (var img in scope) {
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
        }

        var imgF = shadow.Values.MinBy(e => e.LastView);
        return imgF!;
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

    public static void InitClusters(IProgress<string>? progress)
    {
        Debug.WriteLine("Clean-up ids...");
        lock (_lock) {
            var scope = _imgList.Values.Where(img => img.Id > 0).ToArray();
            foreach (var img in scope) {
                img.SetId(0);
            }
        }
        Debug.WriteLine("Looking for the first two cores...");
        Img? imgFirst = null, imgSecond = null;
        float maxDistance = -1f;
        var dtn = DateTime.Now;
        var array = _imgList.Values.ToArray();
        var r1 = Random.Shared.Next(array.Length);
        imgSecond = array[r1];
        do {
            var beam = GetBeam(imgSecond!);
            var distance = beam.Last().Item2;
            if (distance > maxDistance) {
                maxDistance = distance;
                imgFirst = imgSecond;
                if (!TryGet(beam.Last().Item1, out imgSecond)) {
                    throw new Exception("Failed to get the second image by name.");
                }

                Debug.WriteLine($"{imgFirst.Name}-{imgSecond!.Name} {maxDistance:F4}");
            }
            else {
                break;
            }
        } while (true);

        List<Img> imgList;
        lock (_lock) {
            imgList = _imgList.Values.ToList();
        }

        List<Img> clusters = new();

        imgFirst!.SetId(1);
        clusters.Add(imgFirst);
        imgList.Remove(imgFirst);
        UpdateLastViewId(1);

        imgSecond.SetId(2);
        clusters.Add(imgSecond);
        imgList.Remove(imgSecond);
        UpdateLastViewId(2);

        for (var i = 3; i <= 1000; i++) {
            var tmp = new List<Img>(imgList);
            var scope = new List<Img>();
            var size = Math.Min(tmp.Count, 1000000 / i);
            for (var j = 0; j < size; j++) {
                var r = Random.Shared.Next(tmp.Count);
                scope.Add(tmp[r]);
                tmp.RemoveAt(r);
            }

            var distances = new float[clusters.Count, scope.Count];
            Parallel.For(0, clusters.Count, i1 => {
                var clusterVector = clusters[i1].GetVector();
                Parallel.For(0, scope.Count, i2 => {
                    distances[i1, i2] = AppVit.GetDistance(scope[i2].GetVector(), clusterVector);
                });
            });

            var rowMinimums = new float[scope.Count];
            Parallel.For(0, scope.Count, i2 => {
                rowMinimums[i2] = float.MaxValue;
                for (var j = 0; j < clusters.Count; j++) {
                    if (distances[j, i2] < rowMinimums[i2]) {
                        rowMinimums[i2] = distances[j, i2];
                    }
                }
            });

            var maxIndex = -1;
            var maxValue = -1f;
            for (var j = 0; j < rowMinimums.Length; j++) {
                if (rowMinimums[j] > maxValue) {
                    maxValue = rowMinimums[j];
                    maxIndex = j;
                }
            }

            var imgCore = scope[maxIndex];
            imgCore.SetId(i);
            clusters.Add(imgCore);
            imgList.Remove(imgCore);
            Debug.WriteLine($"Cluster #{i}: {imgCore.Name} {maxValue:F4}");
            UpdateLastViewId(i);
        }
    }

    public static int GetPopulation(int id)
    {
        int population;
        lock (_lock) {
            population = _imgList.Count(e => e.Value.Id == id);
        }

        return population;
    }

    public static int GetAvailableId()
    {
        lock (_lock) {
            var ids = new SortedSet<int>();
            foreach (var img in _imgList.Values) {
                if (img.Id > 0) {
                    ids.Add(img.Id);
                }
            }

            var array = ids.ToArray();
            if (array.Length == 0) {
                return 1;
            }

            for (var i = 0; i < array.Length; i++) {
                if (array[i] > i + 1) {
                    return i + 1;
                }
            }

            return array.Length + 1;
        }
    }

    public static int CheckCluster(Img img, List<Tuple<string, float>> beam)
    {
        lock (_lock) {
            var pop = new SortedSet<int>();
            for (var i = 0; i < beam.Count; i++) {
                if (!_imgList.TryGetValue(beam[i].Item1, out var nb) || nb == null) {
                    continue;
                }

                if (nb.Id == 0) {
                    continue;
                }

                if (beam[i].Item2 < AppConsts.MaxSim) {
                    pop.Add(nb.Id);
                }
                else {
                    pop.Remove(nb.Id);
                }
            }

            if (pop.Count == 0) {
                return GetAvailableId();
            }

            var nId = pop.Min();
            if (img.Id == 0) {
                return nId;
            }

            nId  = Math.Min(nId, img.Id);
            return nId;
        }
    }

    public static void UpdateLastViewId(int id)
    {
        if (id == 0) {
            return;
        }

        lock (_lock) {
            _idList[id] = DateTime.Now;
            AppDatabase.UpdateLastViewId(id, _idList[id]);
        }
    }

    public static int[] GetIdsForView()
    {
        lock (_lock) {
            return _idList.OrderBy(e => e.Value).Select(e => e.Key).ToArray();
        }
    }
}