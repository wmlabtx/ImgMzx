using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Processing;
using System.Data;
using System.IO;
using System.Runtime.InteropServices;
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
                $@"SELECT {AppConsts.AttributeMaxImages} FROM {AppConsts.TableVars};",
                _sqlConnection);
            using var reader = command.ExecuteReader();
            if (reader.Read()) {
                _maxImages = (int)reader.GetInt64(0);
            }
        }

        var imageCount = 0;
        lock (_lock) {
            using var countCommand = new SqliteCommand(
                $"SELECT COUNT(*) FROM {AppConsts.TableImages}",
                _sqlConnection);
            imageCount = Convert.ToInt32(countCommand.ExecuteScalar());
        }

        var numVectors = Math.Max(_maxImages + 10000, imageCount + 10000);
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
            var (next, distance, cohortDelta) = FindNearest(img.Vector, hash, history, img.History.Length);
            if (string.IsNullOrEmpty(next)) {
                 return ("no suitable next image found", string.Empty);
            }

            sb.Append($"{distance:F4} ");
            if (cohortDelta > 0) {
                // The subject's own cohort had nothing usable this close.
                sb.Append($"(+{cohortDelta}) ");
            }

            return (next, sb.ToString());
        }
    }

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

        imgY.LastView = DateTime.Now;
        var hsY = imgY.FromHistory;
        if (hsY.Add(hashX)) {
            imgY.ToHistory(hsY);
        }

        UpdateAge(hashX);
    }

    /// <summary>
    /// Increases Age for the given image (epicenter, distance 0 -> increment 1.0) and for
    /// every other image within AgeNeighborRadius, falling off with the square of distance
    /// so only near-duplicates get a meaningful bump. Neighbors are found by a full scan of
    /// the in-memory vectors (parallelized), the writes themselves are a handful of plain
    /// "age = age + @increment" updates batched in one transaction.
    /// </summary>
    public void UpdateAge(string hash)
    {
        lock (_lock) {
            if (!_hashToIndex.TryGetValue(hash, out var selfSlot)) {
                return;
            }

            var query = _vectors.AsSpan(selfSlot * AppConsts.VectorSize, AppConsts.VectorSize).ToArray();
            var capacity = _slotToHash.Length;
            var partitions = Math.Min(Environment.ProcessorCount, Math.Max(1, capacity));

            var updates = new List<(string Hash, float Increment)>();
            var sync = new Lock();

            Parallel.For(
                0, partitions,
                () => new List<(string Hash, float Increment)>(),
                (partition, _, local) => {
                    var from = (int)((long)capacity * partition / partitions);
                    var to = (int)((long)capacity * (partition + 1) / partitions);
                    for (var slot = from; slot < to; slot++) {
                        var candidate = _slotToHash[slot];
                        if (candidate.Length == 0) {
                            continue;
                        }

                        var vector = _vectors.AsSpan(slot * AppConsts.VectorSize, AppConsts.VectorSize);
                        var distance = Vit.ComputeDistance(query, vector);
                        if (distance >= AppConsts.AgeNeighborRadius) {
                            continue;
                        }

                        var scaled = distance / AppConsts.AgeNeighborScale;
                        var increment = 1.0f / (1.0f + scaled * scaled);
                        local.Add((candidate, increment));
                    }

                    return local;
                },
                local => {
                    lock (sync) {
                        updates.AddRange(local);
                    }
                });

            if (updates.Count > AppConsts.AgeMaxUpdates) {
                // Keep the closest ones (largest increment) so the cap bites the long,
                // barely-there tail first, not the near-duplicates that actually matter.
                updates.Sort((a, b) => b.Increment.CompareTo(a.Increment));
                updates.RemoveRange(AppConsts.AgeMaxUpdates, updates.Count - AppConsts.AgeMaxUpdates);
            }

            using var transaction = _sqlConnection.BeginTransaction();
            using var command = _sqlConnection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"UPDATE {AppConsts.TableImages} SET {AppConsts.AttributeAge} = {AppConsts.AttributeAge} + @increment " +
                $"WHERE {AppConsts.AttributeHash} = @hash";
            var incrementParam = command.CreateParameter();
            incrementParam.ParameterName = "@increment";
            command.Parameters.Add(incrementParam);
            var hashParam = command.CreateParameter();
            hashParam.ParameterName = "@hash";
            command.Parameters.Add(hashParam);

            foreach (var (candidateHash, increment) in updates) {
                incrementParam.Value = increment;
                hashParam.Value = candidateHash;
                command.ExecuteNonQuery();
            }
                                       
            transaction.Commit();
        }
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
        UpdateAge(hashX);

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
