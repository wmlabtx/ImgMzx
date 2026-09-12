using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Processing;
using System.Data;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text;

namespace ImgMzx;

public partial class Images(string filedatabase, string filevit) : IDisposable
{
    private readonly Lock _lock = new();
    private bool disposedValue;
    private readonly SqliteConnection _sqlConnection = new();
    private readonly Vit _vit = new(filevit);
    private readonly Panel?[] _imgPanels = { null, null };

    public bool ShowXOR;
    public Vit Vit => _vit;

    // not used
    /*
    public SqliteConnection GetSqliteConnection()
    {
        return _sqlConnection;
    }
    */

    public void Load(IProgress<string>? progress) {
        _sqlConnection.ConnectionString = new SqliteConnectionStringBuilder {
            DataSource = filedatabase,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        _sqlConnection.Open();
        _maxImages = 0;
        lock (_lock) {
            using var command = new SqliteCommand(
                $@"SELECT {AppConsts.AttributeMaxImages}, {AppConsts.AttributeVector} FROM {AppConsts.TableVars};",
                _sqlConnection);
            using var reader = command.ExecuteReader();
            if (reader.Read()) {
                _maxImages = (int)reader.GetInt64(0);

                // A missing or short blob leaves _center zeroed; PickNextSubject
                // then bootstraps it from the first image it drifts towards.
                if (!reader.IsDBNull(1)) {
                    var centerBytes = MemoryMarshal.AsBytes(_center.AsSpan());
                    using var stream = reader.GetStream(1);
                    if (stream.Length == centerBytes.Length) {
                        stream.ReadExactly(centerBytes);
                    }
                }
            }
        }

        var numVectors = _maxImages + 10000;
        _vectors = new float[numVectors * AppConsts.VectorSize];
        _slotToHash = new string[numVectors];
        _historyLength = new int[numVectors];
        Array.Fill(_slotToHash, string.Empty);
        var allVectorsBytes = MemoryMarshal.AsBytes(_vectors.AsSpan());
        var bytesPerVector = AppConsts.VectorSize * sizeof(float);
        var counter = 0;
        lock (_lock) {
            using var command = new SqliteCommand(
                $@"SELECT {AppConsts.AttributeHash}, {AppConsts.AttributeVector}, {AppConsts.AttributeHistory} FROM {AppConsts.TableImages};",
                _sqlConnection);
            using var reader = command.ExecuteReader(CommandBehavior.SequentialAccess);
            var dt = DateTime.Now;
            while (reader.Read()) {
                var hash = reader.GetString(0);
                using (var stream = reader.GetStream(1)) {
                    stream.ReadExactly(allVectorsBytes.Slice(counter * bytesPerVector, bytesPerVector));
                }

                _hashToIndex[hash] = counter;
                _slotToHash[counter] = hash;
                _historyLength[counter] = reader.GetString(2).Length;
                counter++;
                if (DateTime.Now.Subtract(dt).TotalMilliseconds >= AppConsts.TimeLapse) {
                    dt = DateTime.Now;
                    progress?.Report($"Loaded {counter} vectors{AppConsts.CharEllipsis}");
                }
            }
        }

        _freeSlots = new Stack<int>(Enumerable.Range(counter, numVectors - counter).Reverse());
        progress?.Report($"Loaded {counter} vectors");
    }

    public (string Hash, float Distance)[] GetBeam(ReadOnlySpan<float> query)
    {
        lock (_lock) {
            var localQuery = query.ToArray();
            var hashArray = _hashToIndex.Keys.ToArray();
            var results = new (string Hash, float Distance)[hashArray.Length];

            Parallel.For(0, hashArray.Length, i =>
            {
                var hash = hashArray[i];
                var slot = _hashToIndex[hash];
                var vector = _vectors.AsSpan(slot * AppConsts.VectorSize, AppConsts.VectorSize);
                var distance = Vit.ComputeDistance(localQuery, vector);
                results[i] = (hash, distance);
            });

            Array.Sort(results, (a, b) => a.Distance.CompareTo(b.Distance));
            return results;
        }
    }

    public (string next, string message) GetNext(string hash, string? hashD = null)
    {
        var sb = new StringBuilder();

        var img = GetImgFromDatabase(hash);
        if (string.IsNullOrEmpty(img.Hash)) {
            return ("image not found", string.Empty);
        }

        if (img.Vector.Length != AppConsts.VectorSize) {
            var imagedata = AppFile.ReadMex(hash);
            if (imagedata != null) {
                using var image = AppBitmap.GetImage(imagedata);
                 if (image != null) {
                    var vector = _vit.CalculateVector(image);
                    if (vector != null) {
                        img.Vector = vector;
                    }
                }
            }
        }
        
        if (!string.IsNullOrEmpty(hashD)) {
            DeleteImgInDatabase(hashD);
        }

        var history = img.FromHistory;
        lock (_lock) {

            /*
            var changed = false;
            foreach (var h in history) {
                if (ContainsImg(h)) {
                    history.Remove(h);
                    changed = true;
                }
            }

            if (changed) {
                img.ToHistory(history);
            }
            */

            // Only the single best candidate is ever used, so there is no reason to
            // build and sort a full beam - a parallel min-reduction over the vectors is
            // O(n) with no allocation instead of O(n log n) plus two n-sized arrays.
            var (next, distance, cohortDelta) = FindNearest(img.Vector, hash, history, img.History.Length);
            if (string.IsNullOrEmpty(next)) {
                 return ("no suitable next image found", string.Empty);
            }

            img.Distance = distance;

            sb.Append($"{distance:F4} ");
            if (cohortDelta > 0) {
                // The subject's own cohort had nothing usable this close.
                sb.Append($"(+{cohortDelta}) ");
            }

            return (next, sb.ToString());
        }
    }

    /// <summary>
    /// Best live image for <paramref name="query"/>, skipping <paramref name="hash"/>
    /// itself and everything already in <paramref name="history"/>. Candidates are ranked
    /// by distance plus <see cref="AppConsts.HistoryPenalty"/> per history entry of
    /// difference from <paramref name="historyLength"/>: same-cohort images win whenever
    /// they are anywhere near as close, but a cohort of one still finds a partner instead
    /// of failing outright. Ties resolve to the lowest slot so the result does not depend
    /// on how the work was partitioned.
    /// </summary>
    private (string Hash, float Distance, int CohortDelta) FindNearest(
        ReadOnlySpan<float> query, string hash, SortedSet<string> history, int historyLength)
    {
        lock (_lock) {
            if (query.Length != AppConsts.VectorSize) {
                return (string.Empty, 0f, 0);
            }

            // Free slots carry stale vectors; an empty hash in _slotToHash marks them.
            var localQuery = query.ToArray();
            // Flat array + ordinal compare: SortedSet<string>.Contains would run a
            // culture-aware comparison, and history holds a handful of entries at most.
            var excluded = history.ToArray();
            var capacity = _slotToHash.Length;
            var partitions = Math.Min(Environment.ProcessorCount, Math.Max(1, capacity));

            var bestSlot = -1;
            var bestScore = float.MaxValue;
            var bestDistance = 0f;
            var sync = new Lock();

            Parallel.For(
                0, partitions,
                () => (Slot: -1, Score: float.MaxValue, Distance: 0f),
                (partition, _, local) => {
                    var from = (int)((long)capacity * partition / partitions);
                    var to = (int)((long)capacity * (partition + 1) / partitions);
                    for (var slot = from; slot < to; slot++) {
                        var candidate = _slotToHash[slot];
                        if (candidate.Length == 0 || candidate.Equals(hash, StringComparison.Ordinal)) {
                            continue;
                        }

                        var vector = _vectors.AsSpan(slot * AppConsts.VectorSize, AppConsts.VectorSize);
                        var distance = Vit.ComputeDistance(localQuery, vector);

                        // Cohort distance in history entries, not characters.
                        var delta = Math.Abs(_historyLength[slot] - historyLength) / AppConsts.HashLength;
                        var score = distance + (AppConsts.HistoryPenalty * delta);
                        if (score >= local.Score) {
                            continue;
                        }

                        // Deferred until the candidate would actually win, so the
                        // history scan runs O(log n) times instead of once per slot.
                        var seen = false;
                        foreach (var h in excluded) {
                            if (h.Equals(candidate, StringComparison.Ordinal)) {
                                seen = true;
                                break;
                            }
                        }

                        if (!seen) {
                            local = (slot, score, distance);
                        }
                    }

                    return local;
                },
                local => {
                    if (local.Slot < 0) {
                        return;
                    }

                    lock (sync) {
                        if (local.Score < bestScore ||
                            (local.Score == bestScore && local.Slot < bestSlot)) {
                            bestScore = local.Score;
                            bestDistance = local.Distance;
                            bestSlot = local.Slot;
                        }
                    }
                });

            return bestSlot < 0
                ? (string.Empty, 0f, 0)
                : (_slotToHash[bestSlot], bestDistance, Math.Abs(_historyLength[bestSlot] - historyLength) / AppConsts.HashLength);
        }
    }

    public void Find(string? hashX, IProgress<string>? progress)
    {
        do {
            if (string.IsNullOrEmpty(hashX)) {
                hashX = PickNextSubject();
                if (string.IsNullOrEmpty(hashX)) {
                    var totalcount = GetCount();
                    progress?.Report($"totalcount = {totalcount}");
                    return;
                }
            }

            if (!SetLeftPanel(hashX)) {
                DeleteImgInDatabase(hashX);
                hashX = null;
                continue;
            }

            var imgX = GetImgFromDatabase(hashX);
            if (imgX.Vector.Length != AppConsts.VectorSize) {
                progress?.Report($"calculating vector{AppConsts.CharEllipsis}");
                var imagedata = AppFile.ReadMex(hashX);
                if (imagedata != null) {
                    using var image = AppBitmap.GetImage(imagedata);
                    if (image != null) {
                        var vector = _vit.CalculateVector(image);
                        if (vector != null) {
                            imgX.Vector = vector;
                        }
                    }
                }
                else {
                    hashX = null;
                    continue;
                }
            }

            var result = GetNext(hashX);
            var hashY = result.next;
            var message = result.message;
            if (!SetRightPanel(hashY)) {
                hashX = null;
                continue;
            }

            var sb = new StringBuilder();
            var totalimages = GetCount();
            var diff = totalimages - _maxImages;
            var historyCount = GetMinHistoryCount();
            sb.Append($"{historyCount}/{totalimages} ({diff}) ");
            var lastview = Helper.TimeIntervalToString(DateTime.Now.Subtract(imgX.LastView));
            sb.Append($"[{lastview} ago] ");
            sb.Append($"{message} ");
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

        progress?.Report($"Calculating{AppConsts.CharEllipsis}");

        imgX.LastView = DateTime.Now;
        var hsX = imgX.FromHistory;
        if (hsX.Add(hashY)) {
            imgX.ToHistory(hsX);
        }

        (_, _) = GetNext(hashX);

        imgY.LastView = DateTime.Now;
        var hsY = imgY.FromHistory;
        if (hsY.Add(hashX)) {
            imgY.ToHistory(hsY);
        }

        (_, _) = GetNext(hashY);
    }

    public string? DeleteLeft(IProgress<string>? progress)
    {
        progress?.Report($"Calculating{AppConsts.CharEllipsis}");
        var hashX = _imgPanels[0]!.Value.Hash;
        var hashY = _imgPanels[1]!.Value.Hash;
        var vectorX = GetVector(hashX).ToArray();

        AppFile.DeleteMex(hashX, DateTime.Now);
        DeleteImgInDatabase(hashX);

        var imgY = GetImgFromDatabase(hashY);
        imgY.LastView = DateTime.Now;
        GetNext(hashY);   // return value not used, called for its side effects

        return FindClosest(vectorX);
    }

    public string? DeleteRight(IProgress<string>? progress)
    {
        progress?.Report($"Calculating{AppConsts.CharEllipsis}");
        var hashX = _imgPanels[0]!.Value.Hash;
        var hashY = _imgPanels[1]!.Value.Hash;
        var vectorY = GetVector(hashY).ToArray();

        AppFile.DeleteMex(hashY, DateTime.Now);
        DeleteImgInDatabase(hashY);

        var imgX = GetImgFromDatabase(hashX);
        imgX.LastView = DateTime.Now;
        GetNext(hashX);   // return value not used, called for its side effects

        return vectorY.Length == AppConsts.VectorSize ? FindClosest(vectorY) : null;
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

        var rvector = _vit.CalculateVector(image);
        var img = GetImgFromDatabase(hash);
        img.Vector = rvector;
        img.RotateMode = rotatemode;
        img.FlipMode = flipmode;
    }

    public IEnumerable<string> GetAllHashes()
    {
        lock (_lock) {
            return [.. _hashToIndex.Keys];
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue) {
            if (disposing) {
                lock (_lock) {
                    _imgPanels[0]?.Image?.Dispose();
                    _imgPanels[1]?.Image?.Dispose();
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
