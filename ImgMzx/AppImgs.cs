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
    private static SortedList<int, DateTime> _clusterList = new(); // id/lastview

    public static string Status { get; private set; } = string.Empty;

    public static void Load(string filedatabase, IProgress<string>? progress, out int maxImages)
    {
        AppDatabase.Load(
            filedatabase, 
            progress, 
            out SortedList<string, Img> imgList, 
            out SortedList<string, string> nameList,
            out SortedList<int, DateTime> clusterList,
            out maxImages);
        lock (_lock) {
            _imgList = imgList;
            _nameList = nameList;
            _clusterList = clusterList;
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
        var cpop = new List<Tuple<int, int>>();
        lock (_lock) {
            var craw = new int[AppConsts.MaxClusters];
            foreach (var img in _imgList.Values) {
                var id = img.Id;
                if (id == 0) {
                    continue;
                }

                craw[id - 1]++;
            }

            cpop.Clear();
            for (var i = 0; i < craw.Length; i++) {
                cpop.Add(Tuple.Create(i + 1, craw[i]));
            }

            cpop = cpop.OrderByDescending(e => e.Item2).ToList();
            var clast = cpop.Last();
            var idmin = clast.Item1;
            var array = _imgList.Values.Where(e => e.Id == idmin).ToArray();
            var imgX = array.MinBy(e => e.LastCheck);
            return imgX ?? throw new Exception("No image found for check.");
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
            var ids = new SortedList<int, int>();
            foreach (var img in _imgList.Values) {
                if (img.Next.Equals(img.Name) || !_nameList.ContainsKey(img.Next)) {
                    continue;
                }

                if (img.Id == 0) {
                    continue;
                }

                if (ids.ContainsKey(img.Id)) {
                    ids[img.Id]++;
                }
                else {
                    ids.Add(img.Id, 1);
                }
            }

            if (ids.Count == 0) {
                var r = Random.Shared.Next(0, _imgList.Count);
                var imgX = _imgList.Values
                    .ElementAtOrDefault(r);
                return imgX;
            }
            else {
                var id = ids.Keys[0];
                for (var i = 1; i < ids.Count; i++) {
                    var currentId = ids.Keys[i];
                    if (_clusterList[currentId] < _clusterList[id]) {
                        id = currentId;
                    }
                }

                var imgX = _imgList.Values
                    .Where(e => e.Id == id)
                    .MinBy(e => e.LastView);
                _clusterList[id] = DateTime.Now;
                AppDatabase.ClusterUpdateProperty(id, AppConsts.AttributeLastView, _clusterList[id].Ticks);
                return imgX;
            }
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
            if (imgX.Id > 0 && imgY.Id == 0) {
                imgY.SetId(imgX.Id);
            }
            else if (imgX.Id == 0 && imgY.Id > 0) {
                imgX.SetId(imgY.Id);
            }

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

    public static void DeleteAllClusters()
    {
        lock (_lock) {
            _clusterList.Clear();
            AppDatabase.DeleteAllClusters();
        }
    }

    public static void InitClusters(IProgress<string>? progress)
    {
        Debug.WriteLine("Clean-up clusters...");
        DeleteAllClusters();
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
                if (!TryGetByName(beam.Last().Item1, out imgSecond)) {
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

        Add(1);
        AppDatabase.AddCluster(1, DateTime.Now);
        imgFirst!.SetId(1);
        clusters.Add(imgFirst);
        imgList.Remove(imgFirst);

        Add(2);
        AppDatabase.AddCluster(2, DateTime.Now);
        imgSecond.SetId(2);
        clusters.Add(imgSecond);
        imgList.Remove(imgSecond);

        for (var i = 3; i <= AppConsts.MaxClusters; i++) {
            var distances = new float[clusters.Count, imgList.Count];
            Parallel.For(0, clusters.Count, i1 => {
                var clusterVector = clusters[i1].GetVector();
                Parallel.For(0, imgList.Count, i2 => {
                    distances[i1, i2] = AppVit.GetDistance(imgList[i2].GetVector(), clusterVector);
                });
            });

            var rowMinimums = new float[imgList.Count];
            Parallel.For(0, imgList.Count, i2 => {
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

            var imgCore = imgList[maxIndex];
            Add(i);
            AppDatabase.AddCluster(i, DateTime.Now);
            imgCore.SetId(i);
            clusters.Add(imgCore);
            imgList.Remove(imgCore);
            Debug.WriteLine($"Cluster #{i}: {imgCore.Name} {maxValue:F4}");
        }
    }


    public static void Add(int id)
    {
        lock (_lock) {
            _clusterList.Add(id, DateTime.Now);
        }
    }

    public static int ClusterCount()
    {
        int count;
        lock (_lock) {
            count = _clusterList.Count;
        }

        return count;
    }

    public static int GetPopulation(int id)
    {
        int population;
        lock (_lock) {
            if (_clusterList.ContainsKey(id)) {
                population = _imgList.Count(e => e.Value.Id == id);
            }
            else {
                return 0;
            }
        }

        return population;
    }

    public static List<Tuple<int, int>> CheckForEmptyClusters()
    {
        var cpop = new List<Tuple<int, int>>();
        lock (_lock) {
            do {
                var craw = new int[AppConsts.MaxClusters];
                foreach (var img in _imgList.Values) {
                    var id = img.Id;
                    if (id == 0) {
                        continue;
                    }

                    craw[id - 1]++;
                }

                cpop.Clear();
                for (var i = 0; i < craw.Length; i++) {
                    cpop.Add(Tuple.Create(i + 1, craw[i]));
                }

                cpop = cpop.OrderByDescending(e => e.Item2).ToList();
                var cfirst = cpop.First();
                var clast = cpop.Last();
                if (clast.Item2 == 0) {
                    Img? imgFirst = null, imgSecond = null;
                    float maxDistance = -1f;
                    var array = _imgList.Values.Where(e => e.Id == cfirst.Item1).ToArray();
                    var r1 = Random.Shared.Next(array.Length);
                    imgSecond = array[r1];
                    do {
                        var distances = new float[array.Length];
                        var vx = imgSecond.GetVector();
                        Parallel.For(0, distances.Length, i => { distances[i] = AppVit.GetDistance(vx, array[i].GetVector()); });
                        var maxIndex = 0;
                        for (var i = 1; i < distances.Length; i++) {
                            if (distances[i] > distances[maxIndex]) {
                                maxIndex = i;
                            }
                        }

                        var distance = distances[maxIndex];
                        if (distance > maxDistance) {
                            maxDistance = distance;
                            imgFirst = imgSecond;
                            imgSecond = array[maxIndex];
                            Debug.WriteLine($"{imgFirst.Name}-{imgSecond!.Name} {maxDistance:F4}");
                        }
                        else {
                            break;
                        }
                    } while (true);

                    imgFirst!.SetId(clast.Item1);
                }
                else {
                    break;
                }
            } while (true);
        }

        return cpop;
    }

    public static (Img, int) UpdateClusters(Img img, List<Tuple<string, float>> beam)
    {
        lock (_lock) {
            if (img.Id == 0) {
                foreach (var b in beam) {
                    if (!_nameList.TryGetValue(b.Item1, out var hash)) {
                        continue;
                    }

                    if (!_imgList.TryGetValue(hash, out var imgB)) {
                        continue;
                    }

                    var id = imgB!.Id;
                    if (id == 0) {
                        continue;
                    }

                    return (img, id);
                }

                return (img, 0);
            }

            foreach (var b in beam) {
                if (!_nameList.TryGetValue(b.Item1, out var hash)) {
                    continue;
                }

                if (!_imgList.TryGetValue(hash, out var imgB)) {
                    continue;
                }

                var id = imgB!.Id;
                if (img.Id == id) {
                    continue;
                }

                if (id == 0) {
                    return (imgB, img.Id);
                }

                var pops = _imgList.Values.Count(e => e.Id == img.Id);
                var popd = _imgList.Values.Count(e => e.Id == id);
                // TODO
                if (popd - 1 >= pops + 1) {
                    return (imgB, img.Id);
                }
                else if (pops - 1 >= popd + 1) {
                    return (img, id);
                }
            }
        }

        return (img, 0);
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

    /*
    public static List<Tuple<int, int>> GetClusterStatistics()
    {
        int[] s;
        lock (_lock) {
            s = new int[_clusterList.Count];
            foreach (var img in _imgList.Values) {
                if (img.Id > 0) {
                    s[img.Id - 1]++;
                }
            }
        }

        List<Tuple<int, int>> clusterStatistics = new();
        for (var i = 0; i < s.Length; i++) {
            clusterStatistics.Add(Tuple.Create(i + 1, s[i]));
        }

        return clusterStatistics.OrderByDescending(e => e.Item2).ToList();
    }
    */

    public static List<Img> GetImagesByKey(string key)
    {
        lock (_lock) {
            return _imgList.Values
                .Where(img => img.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                .ToList();
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