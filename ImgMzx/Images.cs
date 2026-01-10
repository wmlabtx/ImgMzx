using Microsoft.Data.Sqlite;
using Microsoft.ML.OnnxRuntime;
using SixLabors.ImageSharp.Processing;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace ImgMzx;

public partial class Images : IDisposable
{
    private const int VectorSize = 768;
    private const int InitialCapacity = 200_000;
    private const int ImageSize = 224;
    private const int ChannelSize = ImageSize * ImageSize;
    private const int InputDataSize = 3 * ChannelSize;

    private readonly InferenceSession _session;
    private readonly SessionOptions? _sessionOptions;
    
    private readonly ConcurrentQueue<float[]> _inputDataPool = new();
    private readonly ConcurrentQueue<float[]> _vectorPool = new();
    private readonly ConcurrentQueue<List<NamedOnnxValue>> _containerPool = new();

    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, int> _hashToIndex;
    private readonly SqliteConnection _sqlConnection = new();
    private readonly Panel?[] _imgPanels = { null, null };

    private const int RecentCapacity = 16;
    private readonly List<int> _recent = new();

    private bool disposedValue;

    private int _maxImages;
    private string _lastHash;
    private Memory<float> _vectors;
    private IMemoryOwner<float> _vectorsOwner;
    private string[] _hashes;
    private int _countVectors;
    private int _capacityVectors;

    public bool ShowXOR;

    public int MaxImages => _maxImages;

    public Images(string filedatabase, string filevit)
    {
        _sessionOptions = new SessionOptions {
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
            EnableCpuMemArena = true,
            EnableMemoryPattern = true,
            ExecutionMode = ExecutionMode.ORT_PARALLEL,
            InterOpNumThreads = Environment.ProcessorCount,
            IntraOpNumThreads = Environment.ProcessorCount,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        _sessionOptions.AppendExecutionProvider_CPU();
        _session = new InferenceSession(filevit, _sessionOptions);

        _capacityVectors = InitialCapacity;
        _hashToIndex = new ConcurrentDictionary<string, int>(Environment.ProcessorCount, _capacityVectors, StringComparer.Ordinal);
        _vectorsOwner = MemoryPool<float>.Shared.Rent(_capacityVectors * VectorSize);
        _vectors = _vectorsOwner.Memory[..(_capacityVectors * VectorSize)];
        _hashes = new string[_capacityVectors];
        _countVectors = 0;

        var connectionString = $"Data Source={filedatabase};";
        _sqlConnection = new SqliteConnection(connectionString);
        _sqlConnection.Open();

        using (var pragmaCommand = _sqlConnection.CreateCommand()) {
            pragmaCommand.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA temp_store = MEMORY;";
            pragmaCommand.ExecuteNonQuery();
        }

        _maxImages = InitialCapacity;
        _lastHash = string.Empty;
        var sb = new StringBuilder();
        sb.Append("SELECT ");
        sb.Append($"{AppConsts.AttributeMaxImages},");
        sb.Append($"{AppConsts.AttributeHash}");
        sb.Append($" FROM {AppConsts.TableVars};");
        using (var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection))
        using (var reader = sqlCommand.ExecuteReader()) {
            if (reader.HasRows) {
                while (reader.Read()) {
                    _maxImages = reader.GetInt32(0);
                    _lastHash = reader.GetString(1);
                    break;
                }
            }
        }
    }

    public Panel? GetPanel(int id)
    {
        return (id == 0 || id == 1) ? _imgPanels[id] : null;
    }

    public (string Hash, float Distance)[] GetBeam(ReadOnlySpan<float> query)
    {
        lock (_lock) {
            var results = new (string Hash, float Distance)[_countVectors];
            var localquery = query.ToArray();
            Parallel.For(0, _countVectors, i => {
                var hash = _hashes[i];
                var index = _hashToIndex[hash];
                var vector = GetVectorSpan(index);
                var distance = ComputeDistance(localquery, vector);
                results[i] = (hash, distance);
            });

            Array.Sort(results, (a, b) => a.Distance.CompareTo(b.Distance));
            return results;
        }
    }

    public string GetNext(string hash, string? hashD = null)
    {
        string message;

        var img = GetImgFromDatabase(hash);
        if (img == null) {
            return "image not found";
        }

        var hs = Helper.HistoryFromString(img.Value.History);
        var hsnew = new HashSet<string>(StringComparer.Ordinal);
        foreach (var h in hs) {
            if (ContainsImgInDatabase(h)) {
                hsnew.Add(h);
            }
        }

        if (hs.Count != hsnew.Count) {
            var history = Helper.HistoryToString(hsnew);
            UpdateImgInDatabase(hash, AppConsts.AttributeHistory, history);
            img = GetImgFromDatabase(hash);
            if (img == null) {
                return "image not found";
            }

            hsnew.Clear();
            hs.Clear();
            hs = Helper.HistoryFromString(img.Value.History);
        }

        var oldNext = img.Value.Next;
        if (string.IsNullOrEmpty(oldNext)) {
            oldNext = "XXXX";
        }

        if (!string.IsNullOrEmpty(hashD)) {
            DeleteImgInDatabase(hashD);
            AppFile.DeleteMex(hashD, DateTime.Now);
        }

        lock (_lock) {
            if (!_hashToIndex.TryGetValue(hash, out var index)) {
                return "image not found";
            }

            var vector = GetVectorSpan(index);
            var next = oldNext;
            var distance = 1f;

            var beam = GetBeam(vector);
            for (var i = 0; i < beam.Length; i++) {
                if (beam[i].Hash.Equals(hash)) {
                    continue;
                }

                if (!ContainsImgInDatabase(beam[i].Hash)) {
                    continue;
                }

                if (hs.Contains(beam[i].Hash)) {
                    continue;
                }

                var imgNext = GetImgFromDatabase(beam[i].Hash);
                if (imgNext == null) {
                    continue;
                }

                if (img.Value.Family > 0) {
                    if (img.Value.Family == imgNext.Value.Family) {
                        continue;
                    }
                }
 
                next = beam[i].Hash;
                distance = beam[i].Distance;
                break;
            }

            if (string.IsNullOrEmpty(next)) {
                return "no suitable next image found";
            }

            if (!oldNext.Equals(next) || Math.Abs(img.Value.Distance - distance) >= 0.0001f) {
                message = $"s{img.Value.Score} {oldNext[..4]}{AppConsts.CharEllipsis} {img.Value.Distance:F4} {AppConsts.CharRightArrow} {next[..4]}{AppConsts.CharEllipsis} {distance:F4}";
                UpdateImgInDatabase(hash, AppConsts.AttributeNext, next);
                UpdateImgInDatabase(hash, AppConsts.AttributeDistance, distance);
            }
            else {
                message = $"{distance:F4}";
            }

            UpdateImgInDatabase(hash, AppConsts.AttributeLastCheck, DateTime.Now.Ticks);
            return message;
        }
    }

    public void Find(string? hashX, IProgress<string>? progress)
    {
        for (var i = 0; i < 10; i++) {
            var hashToCheck = GetLastCheckFromDatabase();
            if (hashToCheck == null) {
                 progress?.Report("nothing to show");
                return;
            }

            var imgToCheck = GetImgFromDatabase(hashToCheck);
            if (imgToCheck != null) {
                var message = GetNext(hashToCheck);
                var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(imgToCheck.Value.LastCheck));
                progress?.Report($"[{lastcheck} ago] {hashToCheck[..4]}{AppConsts.CharEllipsis}: {message}");
            }
        }

        var sb = new StringBuilder();
        do {
            sb.Clear();
            var totalimages = GetCountFromDatabase();
            var nearGroup = GetNearGroupFromDatabase();
            var diff = totalimages - _maxImages;
            sb.Append($"{nearGroup}/{totalimages} ({diff}) ");

            if (string.IsNullOrEmpty(hashX)) {
                hashX = GetX();
                if (string.IsNullOrEmpty(hashX)) {
                    var totalcount = GetCountFromDatabase();
                    progress?.Report($"totalcount = {totalcount}");
                    return;
                }
            }

            if (!SetLeftPanel(hashX)) {
                DeleteImgInDatabase(hashX);
                AppFile.DeleteMex(hashX, DateTime.Now);
                hashX = null;
                continue;
            }

            var imgX = GetImgFromDatabase(hashX);
            if (imgX == null) {
                DeleteImgInDatabase(hashX);
                AppFile.DeleteMex(hashX, DateTime.Now);
                hashX = null;
                continue;
            }

            var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(imgX.Value.LastCheck));
            sb.Append($"[{lastcheck} ago] {hashX[..4]}: ");

            var hashY = imgX.Value.Next;
            if (!SetRightPanel(hashY)) {
                var message = GetNext(hashX);
                sb.Append(message);
                imgX = GetImgFromDatabase(hashX);
                if (imgX == null) {
                    DeleteImgInDatabase(hashX);
                    AppFile.DeleteMex(hashX, DateTime.Now);
                    hashX = null;
                    continue;
                }

                hashY = imgX.Value.Next;
                if (!SetRightPanel(hashY)) {
                    hashX = null;
                    continue;
                }
            }
            else {
                sb.Append($"= {imgX.Value.Distance:F4}");
            }

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
        var imgX = GetImgFromDatabase(hashX);
        var hashY = _imgPanels[1]!.Value.Hash;
        var imgY = GetImgFromDatabase(hashY);

        if (imgX != null && imgY != null) {
            UpdateImgInDatabase(hashX, AppConsts.AttributeScore, imgX.Value.Score + 1);
            UpdateImgInDatabase(hashY, AppConsts.AttributeScore, imgY.Value.Score + 1);
            UpdateImgInDatabase(hashX, AppConsts.AttributeLastView, DateTime.Now.Ticks);
            UpdateImgInDatabase(hashY, AppConsts.AttributeLastView, DateTime.Now.Ticks);
            UpdateImgInDatabase(hashX, AppConsts.AttributeLastCheck, DateTime.MinValue.Ticks);
            UpdateImgInDatabase(hashY, AppConsts.AttributeLastCheck, DateTime.MinValue.Ticks);

            var hs = Helper.HistoryFromString(imgX.Value.History);
            hs.Add(hashY);
            var history = Helper.HistoryToString(hs);
            UpdateImgInDatabase(hashX, AppConsts.AttributeHistory, history);

            hs = Helper.HistoryFromString(imgY.Value.History);
            hs.Add(hashX);
            history = Helper.HistoryToString(hs);
            UpdateImgInDatabase(hashY, AppConsts.AttributeHistory, history);

            progress?.Report($"Calculating{AppConsts.CharEllipsis}");
            var message = GetNext(hashX);
            progress?.Report(message);
            message = GetNext(hashY);
            progress?.Report(message);
        }
    }

    public void DeleteLeft(IProgress<string>? progress)
    {
        progress?.Report($"Calculating{AppConsts.CharEllipsis}");
        var hashX = _imgPanels[0]!.Value.Hash;
        var hashY = _imgPanels[1]!.Value.Hash;
        var message = GetNext(hashY, hashX);
        var imgY = GetImgFromDatabase(hashY);
        if (imgY != null) {
            UpdateImgInDatabase(hashY, AppConsts.AttributeScore, imgY.Value.Score + 1);
            UpdateImgInDatabase(hashY, AppConsts.AttributeLastView, DateTime.Now.Ticks);
            UpdateImgInDatabase(hashY, AppConsts.AttributeLastCheck, DateTime.MinValue.Ticks);
        }

        progress?.Report(message);
    }

    public void DeleteRight(IProgress<string>? progress)
    {
        progress?.Report($"Calculating{AppConsts.CharEllipsis}");
        var hashX = _imgPanels[0]!.Value.Hash;
        var hashY = _imgPanels[1]!.Value.Hash;
        var message = GetNext(hashX, hashY);
        var imgX = GetImgFromDatabase(hashX);
        if (imgX != null) {
            UpdateImgInDatabase(hashX, AppConsts.AttributeScore, imgX.Value.Score + 1);
            UpdateImgInDatabase(hashX, AppConsts.AttributeLastView, DateTime.Now.Ticks);
            UpdateImgInDatabase(hashX, AppConsts.AttributeLastCheck, DateTime.MinValue.Ticks);
        }

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

        var rvector = CalculateVector(image);
        ChangeVector(hash, rvector);
        UpdateImgInDatabase(hash, AppConsts.AttributeRotateMode, (int)rotatemode);
        UpdateImgInDatabase(hash, AppConsts.AttributeFlipMode, (int)flipmode);
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

                    _vectorsOwner?.Dispose();
                    _hashToIndex.Clear();
                    Array.Clear(_hashes);
                    _countVectors = 0;
                    _capacityVectors = 0;
                    _sqlConnection?.Dispose();
                    _session?.Dispose();
                    _sessionOptions?.Dispose();

                    while (_inputDataPool.TryDequeue(out _)) { }
                    while (_vectorPool.TryDequeue(out _)) { }
                    while (_containerPool.TryDequeue(out _)) { }
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