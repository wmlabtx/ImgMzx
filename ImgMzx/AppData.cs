using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Processing;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ImgMzx;

/*
public sealed class AppData : IDisposable
{
    private const int MaxElements = 10000;
    private const int VectorSize = AppConsts.VectorSize; // 768

    private readonly float[] _vectors; // Pre-allocated: 1000 * 768 floats
    private readonly Dictionary<string, int> _hashToSlot; // hash -> slot index
    private readonly bool[] _occupiedSlots; // track which slots are used
    private readonly object _lock = new();
    private int _count;
    private bool _disposed;

    private readonly SqliteConnection _sqlConnection = new();
    private readonly int _maxImages;

    public AppData(string filedatabase)
    {
        _vectors = new float[MaxElements * VectorSize];
        _hashToSlot = new Dictionary<string, int>(MaxElements, StringComparer.Ordinal);
        _occupiedSlots = new bool[MaxElements];
        _count = 0;

        lock (_lock) {
            var connectionString = $"Data Source={filedatabase};";
            _sqlConnection = new SqliteConnection(connectionString);
            _sqlConnection.Open();

            using (var pragmaCommand = _sqlConnection.CreateCommand()) {
                pragmaCommand.CommandText = "PRAGMA foreign_keys = ON";
                pragmaCommand.ExecuteNonQuery();
            }

            _maxImages = 200_000;
            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append($"{AppConsts.AttributeMaxImages}");
            sb.Append($" FROM {AppConsts.TableVars};");
            using (var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection)) {
                using (var reader = sqlCommand.ExecuteReader()) {
                    if (reader.HasRows) {
                        while (reader.Read()) {
                            _maxImages = reader.GetInt32(0);
                            break;
                        }
                    }
                }
            }
        }
    }

    public void LoadVectors(IProgress<string>? progress)
    {
        lock (_lock) {
            if (_sqlConnection?.State != System.Data.ConnectionState.Open) {
                return;
            }

            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append($"{AppConsts.AttributeHash},"); // 0
            sb.Append($"{AppConsts.AttributeVector}"); // 1
            sb.Append($" FROM {AppConsts.TableImages};");

            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            using var reader = sqlCommand.ExecuteReader();

            var dt = DateTime.Now;
            while (reader.Read()) {
                var hash = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var vector = reader.IsDBNull(1) ? [] : Helper.ArrayToFloat((byte[])reader[1]);
                AppVars.Vectors.AddVector(hash, vector);

                if (DateTime.Now.Subtract(dt).TotalMilliseconds > AppConsts.TimeLapse) {
                    dt = DateTime.Now;
                    progress?.Report($"Loaded {AppVars.Vectors.Count} vectors{AppConsts.CharEllipsis}");
                }
            }
        }
    }

    public void Delete(string hash)
    {
        AppFile.DeleteMex(hash, DateTime.Now);
        DeleteImg(hash);
        TryRemove(hash);
    }

    public void UpdateMaxImages(int maxImages)
    {
        lock (_lock) {
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.Connection = _sqlConnection;
            sqlCommand.CommandText =
                $"UPDATE {AppConsts.TableVars} SET {AppConsts.AttributeMaxImages} = @{AppConsts.AttributeMaxImages}";
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeMaxImages}", maxImages);
            sqlCommand.ExecuteNonQuery();
        }
    }

    public string GetRandomHash()
    {
        lock (_lock) {
            if (_sqlConnection.State != System.Data.ConnectionState.Open) {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.Append($"SELECT {AppConsts.AttributeHash} ");
            sb.Append($"FROM {AppConsts.TableImages} ");
            sb.Append($"ORDER BY RANDOM() ");
            sb.Append($"LIMIT 1;");

            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            using var reader = sqlCommand.ExecuteReader();
            if (reader.HasRows && reader.Read()) {
                return reader.GetString(0);
            }

            return string.Empty;
        }
    }

    public string[] GetHashForWindow()
    {
        lock (_lock) {
            if (_sqlConnection.State != System.Data.ConnectionState.Open) {
                return [];
            }

            var sb = new StringBuilder();
            sb.Append($"SELECT {AppConsts.AttributeHash} ");
            sb.Append($"FROM {AppConsts.TableImages} ");
            sb.Append($"ORDER BY score DESC, RANDOM() ");
            sb.Append($"LIMIT {MaxElements}");

            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            using var reader = sqlCommand.ExecuteReader();

            var hashes = new List<string>();
            while (reader.Read()) {
                var hash = reader.GetString(0);
                if (!string.IsNullOrEmpty(hash)) {
                    hashes.Add(hash);
                }
            }

            return [.. hashes];
        }
    }

    /// <summary>
    /// Get total count of rows in the images table.
    /// </summary>
    public int Count()
    {
        lock (_lock) {
            if (_sqlConnection?.State != System.Data.ConnectionState.Open) {
                return 0;
            }

            var sb = new StringBuilder();
            sb.Append($"SELECT COUNT(*) FROM {AppConsts.TableImages};");

            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            using var reader = sqlCommand.ExecuteReader();

            if (reader.HasRows && reader.Read()) {
                return reader.GetInt32(0);
            }

            return 0;
        }
    }

    public Img? GetImg(string hash)
    {
        lock (_lock) {
            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append($"{AppConsts.AttributeRotateMode},"); // 0
            sb.Append($"{AppConsts.AttributeFlipMode},"); // 1
            sb.Append($"{AppConsts.AttributeLastView},"); // 2
            sb.Append($"{AppConsts.AttributeNext},"); // 3
            sb.Append($"{AppConsts.AttributeScore},"); // 4
            sb.Append($"{AppConsts.AttributeLastCheck},"); // 5
            sb.Append($"{AppConsts.AttributeDistance},"); // 6
            sb.Append($"{AppConsts.AttributeHash}"); // 7
            sb.Append($" FROM {AppConsts.TableImages}");
            sb.Append($" WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}");
            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
            using var reader = sqlCommand.ExecuteReader();
            if (reader.HasRows) {
                while (reader.Read()) {
                    var img = new Img {
                        RotateMode = Enum.Parse<RotateMode>(reader.GetInt64(0).ToString()),
                        FlipMode = Enum.Parse<FlipMode>(reader.GetInt64(1).ToString()),
                        LastView = new DateTime(reader.GetInt64(2)),
                        Next = reader.GetString(3),
                        Score = (int)reader.GetInt64(4),
                        LastCheck = new DateTime(reader.GetInt64(5)),
                        Distance = reader.GetFloat(6),
                        Hash = reader.IsDBNull(7) ? string.Empty : reader.GetString(7)
                    };

                    return img;
                }
            }

            return null;
        }
    }

    public bool ContainsImg(string hash)
    {
        lock (_lock) {
            var sb = new StringBuilder();
            sb.Append($"SELECT 1 FROM {AppConsts.TableImages}");
            sb.Append($" WHERE {AppConsts.AttributeHash} = @hash LIMIT 1;");
            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            sqlCommand.Parameters.AddWithValue("@hash", hash);
            using var reader = sqlCommand.ExecuteReader();
            return reader.HasRows;
        }
    }

    public void AddImg(Img img)
    {
        lock (_lock) {
            using (var sqlCommand = _sqlConnection.CreateCommand()) {
                sqlCommand.Connection = _sqlConnection;
                var sb = new StringBuilder();
                sb.Append($"INSERT INTO {AppConsts.TableImages} (");
                sb.Append($"{AppConsts.AttributeHash},");
                sb.Append($"{AppConsts.AttributeRotateMode},");
                sb.Append($"{AppConsts.AttributeFlipMode},");
                sb.Append($"{AppConsts.AttributeLastView},");
                sb.Append($"{AppConsts.AttributeNext},");
                sb.Append($"{AppConsts.AttributeScore},");
                sb.Append($"{AppConsts.AttributeLastCheck},");
                sb.Append($"{AppConsts.AttributeDistance},");
                sb.Append($"{AppConsts.AttributeVector},");
                sb.Append(") VALUES (");
                sb.Append($"@{AppConsts.AttributeHash},");
                sb.Append($"@{AppConsts.AttributeRotateMode},");
                sb.Append($"@{AppConsts.AttributeFlipMode},");
                sb.Append($"@{AppConsts.AttributeLastView},");
                sb.Append($"@{AppConsts.AttributeNext},");
                sb.Append($"@{AppConsts.AttributeScore},");
                sb.Append($"@{AppConsts.AttributeLastCheck},");
                sb.Append($"@{AppConsts.AttributeDistance},");
                sb.Append($"@{AppConsts.AttributeVector}");
                sb.Append(')');
                sqlCommand.CommandText = sb.ToString();
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", img.Hash);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeRotateMode}", (int)img.RotateMode);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeFlipMode}", (int)img.FlipMode);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastView}", img.LastView.Ticks);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeNext}", img.Next);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeScore}", img.Score);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastCheck}", img.LastCheck.Ticks);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeDistance}", img.Distance);
                var buffer = MemoryMarshal.AsBytes(img.Vector.AsSpan());
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVector}", buffer);
                sqlCommand.ExecuteNonQuery();
            }
        }
    }

    public void DeleteImg(string hash)
    {
        lock (_lock) {
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.Connection = _sqlConnection;
            sqlCommand.CommandText =
                $"DELETE FROM {AppConsts.TableImages} WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}";
            sqlCommand.Parameters.Clear();
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
            sqlCommand.ExecuteNonQuery();
        }
    }

    public void UpdateImg(string hash, string key, object val)
    {
        lock (_lock) {
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.CommandText = 
                $"UPDATE {AppConsts.TableImages} SET {key} = @value WHERE {AppConsts.AttributeHash} = @hash";
            sqlCommand.Parameters.AddWithValue("@value", val ?? DBNull.Value);
            sqlCommand.Parameters.AddWithValue("@hash", hash);
            sqlCommand.ExecuteNonQuery();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindFreeSlot()
    {
        for (var i = 0; i < MaxElements; i++) {
            if (!_occupiedSlots[i]) {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Add vector to collection. Returns true if added successfully.
    /// </summary>
    public bool TryAdd(string hash, ReadOnlySpan<float> vector)
    {
        if (string.IsNullOrEmpty(hash) || hash.Length != AppConsts.HashLength)
            return false;

        if (vector.Length != VectorSize)
            return false;

        lock (_lock) {
            if (_disposed || _hashToSlot.ContainsKey(hash) || _count >= MaxElements)
                return false;

            var slotIndex = FindFreeSlot();
            if (slotIndex == -1) {
                return false;
            }

            // Copy vector to slot
            var offset = slotIndex * VectorSize;
            vector.CopyTo(_vectors.AsSpan(offset, VectorSize));

            _hashToSlot[hash] = slotIndex;
            _occupiedSlots[slotIndex] = true;
            _count++;
            return true;
        }
    }

    public bool TryAdd(string hash, float[] vector)
    {
        if (vector == null) {
            return false;
        }

        return TryAdd(hash, vector.AsSpan());
    }

    /// <summary>
    /// Check if hash exists in collection.
    /// </summary>
    public bool ContainsKey(string hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;

        lock (_lock) {
            return !_disposed && _hashToSlot.ContainsKey(hash);
        }
    }

    /// <summary>
    /// Get vector by hash. Returns null if not found.
    /// </summary>
    public float[]? TryGetVector(string hash)
    {
        if (string.IsNullOrEmpty(hash)) {
            return null;
        }

        lock (_lock) {
            if (_disposed || !_hashToSlot.TryGetValue(hash, out var slotIndex)) {
                return null;
            }

            var result = new float[VectorSize];
            var offset = slotIndex * VectorSize;
            _vectors.AsSpan(offset, VectorSize).CopyTo(result.AsSpan());
            return result;
        }
    }

    /// <summary>
    /// Get vector as span (zero-copy). Use within lock if needed for thread safety.
    /// </summary>
    public ReadOnlySpan<float> GetVectorSpan(string hash)
    {
        if (string.IsNullOrEmpty(hash)) return ReadOnlySpan<float>.Empty;

        if (!_hashToSlot.TryGetValue(hash, out var slotIndex))
            return ReadOnlySpan<float>.Empty;

        var offset = slotIndex * VectorSize;
        return _vectors.AsSpan(offset, VectorSize);
    }

    /// <summary>
    /// Remove vector by hash.
    /// </summary>
    public bool TryRemove(string hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;

        lock (_lock) {
            if (_disposed || !_hashToSlot.TryGetValue(hash, out var slotIndex)) {
                return false;
            }

            _hashToSlot.Remove(hash);
            _occupiedSlots[slotIndex] = false;
            _count--;

            // Clear vector data (optional, for security)
            var offset = slotIndex * VectorSize;
            _vectors.AsSpan(offset, VectorSize).Clear();

            return true;
        }
    }

    /// <summary>
    /// Get all hash keys.
    /// </summary>
    public string[] GetAllKeys()
    {
        lock (_lock) {
            if (_disposed) {
                return Array.Empty<string>();
            }
            
            return _hashToSlot.Keys.ToArray();
        }
    }

    /// <summary>
    /// Compute similarity beam for given hash.
    /// </summary>
    public (string Hash, float Distance)[] GetBeam(float[] vector)
    {
        if (vector == null || vector.Length != VectorSize)
            return [];

        lock (_lock) {
            if (_disposed || _count == 0)
                return [];

            // Copy the query vector to a local array to avoid ref struct issues in lambda
            var queryArray = new float[VectorSize];
            Array.Copy(vector, queryArray, VectorSize);

            var occupiedSlots = new List<int>(_count);
            for (int i = 0; i < MaxElements; i++) {
                if (_occupiedSlots[i]) {
                    occupiedSlots.Add(i);
                }
            }

            var results = new (string Hash, float Distance)[occupiedSlots.Count];
            Parallel.For(0, occupiedSlots.Count, i =>
            {
                var targetSlot = occupiedSlots[i];
                var targetOffset = targetSlot * VectorSize;
                var targetSpan = _vectors.AsSpan(targetOffset, VectorSize);
                var distance = ComputeDistance(queryArray.AsSpan(), targetSpan);
                var targetHash = _hashToSlot.First(kvp => kvp.Value == targetSlot).Key;
                results[i] = (targetHash, distance);
            });

            return results.OrderBy(x => x.Distance).ToArray();
        }
    }

    public int WindowCount {
        get {
            lock (_lock) {
                return _disposed ? 0 : _count;
            }
        }
    }

    public int MaxImages => _maxImages;

    /// <summary>
    /// Clear all data.
    /// </summary>
    public void Clear()
    {
        lock (_lock) {
            if (_disposed) {
                return;
            }

            _hashToSlot.Clear();
            Array.Clear(_occupiedSlots);
            Array.Clear(_vectors);
            _count = 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ComputeDistance(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        if (x.Length != y.Length) return 1f;

        var dotProduct = 0f;
        for (int i = 0; i < x.Length; i++) {
            dotProduct += x[i] * y[i];
        }

        var distance = 1f - dotProduct;
        return Math.Max(0f, Math.Min(1f, distance));
    }

    public DateTime GetMinimalLastView()
    {
        lock (_lock) {
            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append($"MIN({AppConsts.AttributeLastView})");
            sb.Append($" FROM {AppConsts.TableImages};");

            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            using var reader = sqlCommand.ExecuteReader();

            if (reader.HasRows && reader.Read()) {
                if (!reader.IsDBNull(0)) {
                    var minLastViewTicks = reader.GetInt64(0);
                    return new DateTime(minLastViewTicks);
                }
            }

            return new DateTime(1980, 1, 1);
        }
    }

    public string GetLastCheck()
    {
        lock (_lock) {
            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append($"{AppConsts.AttributeHash} ");
            sb.Append($"FROM {AppConsts.TableImages} ");
            sb.Append($"WHERE {AppConsts.AttributeLastCheck} = (");
            sb.Append($"  SELECT MIN({AppConsts.AttributeLastCheck}) ");
            sb.Append($"  FROM {AppConsts.TableImages}");
            sb.Append(") LIMIT 1;");

            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            using var reader = sqlCommand.ExecuteReader();

            if (reader.HasRows && reader.Read()) {
                return reader.GetString(0);
            }

            return string.Empty;
        }
    }

    public string? GetX(IProgress<string>? progress)
    {
        lock (_lock) {
            var sb1 = new StringBuilder();
            sb1.Append("SELECT ");
            sb1.Append($"(MAX({AppConsts.AttributeLastView}) - MIN({AppConsts.AttributeLastView})) as e ");
            sb1.Append($"FROM {AppConsts.TableImages};");

            long e;
            using (var cmd1 = new SqliteCommand(sb1.ToString(), _sqlConnection))
            using (var reader1 = cmd1.ExecuteReader()) {
                if (!reader1.HasRows || !reader1.Read()) {
                    return string.Empty;
                }

                e = reader1.GetInt64(0);
            }

            var sb2 = new StringBuilder();
            sb2.Append($"SELECT {AppConsts.AttributeHash} ");
            sb2.Append($"FROM {AppConsts.TableImages} ");
            sb2.Append($"ORDER BY ({AppConsts.AttributeLastView} + (RANDOM() % @e)) ASC ");
            sb2.Append($"LIMIT 1;");

            using var cmd2 = new SqliteCommand(sb2.ToString(), _sqlConnection);
            cmd2.Parameters.AddWithValue("@e", e);
            using var reader2 = cmd2.ExecuteReader();

            if (reader2.HasRows && reader2.Read()) {
                return reader2.GetString(0);
            }

            return null;
        }
    }

    public (string, string) GetNext(string hash, string? hashD = null)
    {
        string message;

        try {
            var img = GetImg(hash);
            if (img == null) {
                return (string.Empty, "image not found");
            }

            var oldDistance = img.Value.Distance;
            var oldNext = img.Value.Next;
            if (string.IsNullOrEmpty(oldNext)) {
                oldNext = "XXXX";
            }

            if (!string.IsNullOrEmpty(hashD)) {
                Delete(hashD);
            }

            var vector = AppVars.Data.TryGetVector(hash);
            if (vector == null || vector.Length != AppConsts.VectorSize) {
                var imagedata = AppFile.ReadMex(hash);
                if (imagedata == null) {
                    Delete(hash);
                    return (string.Empty, "bad image");
                }
                else {
                    using (var image = AppBitmap.GetImage(imagedata)) {
                        if (image == null) {
                            Delete(hash);
                            return (string.Empty, "bad image");
                        }
                        else {
                            vector = AppVit.GetVector(image);
                            AppVars.Vectors.ChangeVector(hash, vector);
                            
                        }
                    }
                }
            }

            var next = oldNext;
            var distance = oldDistance;
            if (!AppHash.IsValidHash(next) || !ContainsImg(hash)) {
                distance = 1f;
            }

            var beam = GetBeam(vector);
            for (var i = 0; i < beam.Length; i++) {
                if (beam[i].Hash.Equals(hash)) {
                    continue;
                }

                if (beam[i].Distance < distance) {
                    next = beam[i].Hash;
                    distance = beam[i].Distance;
                }

                break;
            }

            if (string.IsNullOrEmpty(next)) {
                return (string.Empty, "no suitable next image found");
            }

            if (!oldNext.Equals(next) ||  Math.Abs(oldDistance - distance) >= 0.0001f) {
                message = $"{oldNext[..4]} {oldDistance:F4} {AppConsts.CharRightArrow} {next[..4]} {distance:F4}";
                UpdateImg(hash, AppConsts.AttributeNext, next);
                UpdateImg(hash, AppConsts.AttributeDistance, distance);
            }
            else {
                message = $"{distance:F4}";
            }

            UpdateImg(hash, AppConsts.AttributeLastCheck, DateTime.Now.Ticks);

            return (next, message);
        }
        catch (Exception ex) {
            return (string.Empty, $"error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get all images from the images table.
    /// </summary>
    public Img[] GetAllImgs()
    {
        lock (_lock) {
            if (_sqlConnection?.State != System.Data.ConnectionState.Open) {
                return [];
            }

            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append($"{AppConsts.AttributeRotateMode},"); // 0
            sb.Append($"{AppConsts.AttributeFlipMode},"); // 1
            sb.Append($"{AppConsts.AttributeLastView},"); // 2
            sb.Append($"{AppConsts.AttributeNext},"); // 3
            sb.Append($"{AppConsts.AttributeScore},"); // 4
            sb.Append($"{AppConsts.AttributeLastCheck},"); // 5
            sb.Append($"{AppConsts.AttributeDistance},"); // 6
            sb.Append($"{AppConsts.AttributeHash},"); // 7
            sb.Append($"{AppConsts.AttributeVector}"); // 8
            sb.Append($" FROM {AppConsts.TableImages};");

            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            using var reader = sqlCommand.ExecuteReader();

            var images = new List<Img>();
            while (reader.Read()) {
                var img = new Img {
                    RotateMode = Enum.Parse<RotateMode>(reader.GetInt64(0).ToString()),
                    FlipMode = Enum.Parse<FlipMode>(reader.GetInt64(1).ToString()),
                    LastView = new DateTime(reader.GetInt64(2)),
                    Next = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Score = (int)reader.GetInt64(4),
                    LastCheck = new DateTime(reader.GetInt64(5)),
                    Distance = reader.GetFloat(6),
                    Hash = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    Vector = reader.IsDBNull(8) ? [] : Helper.ArrayToFloat((byte[])reader[8])
                };

                images.Add(img);
            }

            return [.. images];
        }
    }

    /// <summary>
    /// Read vector from database for the given hash.
    /// </summary>
    public float[]? ReadVectorFromDatabase(string hash)
    {
        if (string.IsNullOrEmpty(hash))
            return null;

        lock (_lock) {
            if (_sqlConnection?.State != System.Data.ConnectionState.Open) {
                return null;
            }

            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append($"{AppConsts.AttributeVector} ");
            sb.Append($"FROM {AppConsts.TableImages} ");
            sb.Append($"WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash};");

            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
            using var reader = sqlCommand.ExecuteReader();

            if (reader.HasRows && reader.Read()) {
                if (!reader.IsDBNull(0)) {
                    var vectorBytes = (byte[])reader[0];
                    if (vectorBytes.Length == AppConsts.VectorSize * sizeof(float)) {
                        return Helper.ArrayToFloat(vectorBytes);
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Save vector to database for the given hash.
    /// </summary>
    public void SaveVectorToDatabase(string hash, float[] vector)
    {
        if (string.IsNullOrEmpty(hash) || vector == null || vector.Length != AppConsts.VectorSize)
            return;

        lock (_lock) {
            if (_sqlConnection?.State != System.Data.ConnectionState.Open) {
                return;
            }

            var vectorBytes = MemoryMarshal.AsBytes(vector.AsSpan());
            
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.CommandText = 
                $"UPDATE {AppConsts.TableImages} SET {AppConsts.AttributeVector} = @vector WHERE {AppConsts.AttributeHash} = @hash";
            sqlCommand.Parameters.AddWithValue("@vector", vectorBytes.ToArray()); // SQLite needs byte[]
            sqlCommand.Parameters.AddWithValue("@hash", hash);
            sqlCommand.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        lock (_lock) {
            if (_disposed) return;

            _hashToSlot.Clear();
            Array.Clear(_occupiedSlots);
            Array.Clear(_vectors);
            _count = 0;
            _disposed = true;
        }

        lock (_lock) {
            _sqlConnection?.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
*/