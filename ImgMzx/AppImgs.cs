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

    public static List<Tuple<int, int, long, long>> GetClusters()
    {
        var clusters = new List<Tuple<int, int, long, long>>();
        lock (_lock) {
            var cpop = new int[AppConsts.MaxClusters];
            var clc = new long[AppConsts.MaxClusters];
            foreach (var img in _imgList.Values) {
                var id = img.Id;
                if (id == 0) {
                    continue;
                }

                cpop[id - 1]++;
                if (clc[id - 1] == 0 || img.LastCheck.Ticks > clc[id - 1]) {
                    clc[id - 1] = img.LastCheck.Ticks;
                }
            }

            for (var i = 0; i < cpop.Length; i++) {
                var lv = _clusterList.ContainsKey(i + 1) ? _clusterList[i + 1].Ticks : 0;
                clusters.Add(Tuple.Create(i + 1, cpop[i], clc[i], lv));
            }
        }

        return clusters;
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
        var clusters = GetClusters().Where(e => e.Item2 > 0).ToList();
        if (clusters.Count == 0) {
            throw new Exception("No clusters found for check.");
        }

        var cluster = clusters.MinBy(e => e.Item3);
        if (cluster == null) {
            throw new Exception("No clusters found for check.");
        }

        lock (_lock) {
            var array = _imgList.Values.Where(e => e.Id == cluster.Item1).ToArray();
            var imgX = array.MinBy(e => e.LastCheck);
            if (imgX == null) {
                throw new Exception("No images found for check.");
            }

            return imgX;
        }
    }

    public static Img GetForView()
    {
        var clusters = GetClusters().Where(e => e.Item2 > 0).ToList();
        if (clusters.Count == 0) {
            throw new Exception("No clusters found for view.");
        }

        var cluster = clusters.MinBy(e => e.Item4);
        if (cluster == null) {
            throw new Exception("No clusters found for view.");
        }

        lock (_lock) {
            var array = _imgList.Values.Where(e => e.Id == cluster.Item1).ToArray();
            var imgX = array.MinBy(e => e.LastView);
            if (imgX == null) {
                throw new Exception("No images found for view.");
            }

            return imgX;
        }
    }

    public static Img GetX(IProgress<string>? progress)
    {
        var imgX = GetForView();
        _clusterList[imgX.Id] = DateTime.Now;
        AppDatabase.ClusterUpdateProperty(imgX.Id, AppConsts.AttributeLastView, _clusterList[imgX.Id]);
        return imgX;
    }

    public static Img GetY(Img imgX, IProgress<string>? progress)
    {
        var beam = GetBeam(imgX);
        if (beam.Count == 0) {
            throw new Exception("No images found for beam.");
        }

        if (!TryGet(beam.First().Item1, out var imgY)) {
            throw new Exception("Failed to get image by hash.");
        }

        if (imgY == null) {
            throw new Exception("Failed to get image by hash.");
        }

        var sb = new StringBuilder();
        lock (_lock) {
            var diff = _imgList.Count - AppVars.MaxImages;
            sb.Append($"{_imgList.Count} ({diff})");
        }

        Status = sb.ToString();

        return imgY;
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

    public static (Img, int) CheckCluster(Img img, List<Tuple<string, float>> beam)
    {
        lock (_lock) {
            if (img.Id == 0) {
                foreach (var b in beam) {
                    if (!_imgList.TryGetValue(b.Item1, out var imgB)) {
                        continue;
                    }

                    var id = imgB!.Id;
                    if (id == 0) {
                        continue;
                    }

                    return (img, id);
                }

                throw new Exception("No clusters found for the image.");
            }

            foreach (var b in beam) {
                if (!_imgList.TryGetValue(b.Item1, out var imgB)) {
                    continue;
                }

                var id = imgB!.Id;
                if (id == img.Id) {
                    continue;
                }
 
                if (id == 0) {
                    return (imgB, img.Id);
                }

                var pop = _imgList.Values.Count(e => e.Id == img.Id);
                var popB = _imgList.Values.Count(e => e.Id == id);
                if (pop - 1 > popB + 1) {
                    return (img, id);
                }

                if (popB - 1 > pop + 1) {
                    return (imgB, img.Id);
                }

                return (img, img.Id);
            }

            throw new Exception("No clusters found for the image.");
        }
    }

    public static int FindCluster(float[] vector)
    {
        List<Img> shadow;
        lock (_lock) {
            shadow = new List<Img>(_imgList.Values);
        }

        var distances = new float[shadow.Count];
        Parallel.For(0, distances.Length, i => { distances[i] = AppVit.GetDistance(vector, shadow[i].GetVector()); });

        var beam = new List<Tuple<string, float>>(shadow.Count);
        for (var i = 0; i < shadow.Count; i++) {
            beam.Add(Tuple.Create(shadow[i].Hash, distances[i]));
        }

        beam = beam.OrderBy(e => e.Item2).ToList();

        int id;
        lock (_lock) {
            foreach (var b in beam) {
                if (!_imgList.TryGetValue(b.Item1, out var imgB)) {
                    continue;
                }

                id = imgB!.Id;
                if (id == 0) {
                    continue;
                }

                return id;
            }
        }

        throw new Exception("No clusters found for the image.");
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
}