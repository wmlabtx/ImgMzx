using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Processing;
using System.Runtime.InteropServices;

namespace ImgMzx;

public partial class Images : IDisposable
{
    public int GetCount()
    {
        lock (_lock) {
            using var command = new SqliteCommand(
                $"SELECT COUNT(*) FROM {AppConsts.TableImages}", 
                _sqlConnection);
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    public bool ContainsImg(string hash)
    {
        lock (_lock) {
            using var command = new SqliteCommand(
                $"SELECT COUNT({AppConsts.AttributeHash}) FROM {AppConsts.TableImages} WHERE {AppConsts.AttributeHash} = @hash",
                _sqlConnection);
            command.Parameters.AddWithValue("@hash", hash);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }
    }

    public void UpdateVectorInDatabase(string hash, ReadOnlySpan<float> vector)
    {
        lock (_lock) {
            using var sqlCommand = new SqliteCommand(
                $"UPDATE {AppConsts.TableImages} SET {AppConsts.AttributeVector} = @value WHERE {AppConsts.AttributeHash} = @hash",
                _sqlConnection);
            sqlCommand.Parameters.AddWithValue("@value", MemoryMarshal.Cast<float, byte>(vector).ToArray());
            sqlCommand.Parameters.AddWithValue("@hash", hash);
            sqlCommand.ExecuteNonQuery();
        }
    }

    public void UpdateImgInDatabase(string hash, string key, object val)
    {
        lock (_lock) {
            // Single choke point for every column write, so the in-memory history
            // mirror used by GetNext/FindClosest cannot drift away from the database.
            if (key == AppConsts.AttributeHistory && _hashToIndex.TryGetValue(hash, out var cachedSlot)) {
                _historyLength[cachedSlot] = ((string?)val)?.Length ?? 0;
            }

            using var sqlCommand = new SqliteCommand(
                $"UPDATE {AppConsts.TableImages} SET {key} = @value WHERE {AppConsts.AttributeHash} = @hash",
                _sqlConnection);
            sqlCommand.Parameters.AddWithValue("@value", val ?? DBNull.Value);
            sqlCommand.Parameters.AddWithValue("@hash", hash);
            sqlCommand.ExecuteNonQuery();
        }
    }

    public void AddImgToDatabase(Img img, Span<float> vector)
    {
        lock (_lock) {
            AddVector(img.Hash, vector, img.History.Length);
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.CommandText = $@"
            INSERT INTO {AppConsts.TableImages} (
                {AppConsts.AttributeHash},
                {AppConsts.AttributeRotateMode},
                {AppConsts.AttributeFlipMode},
                {AppConsts.AttributeLastView},
                {AppConsts.AttributeHistory},
                {AppConsts.AttributeVector},
                {AppConsts.AttributeRate},
                {AppConsts.AttributeDistance}
            ) VALUES (
                @{AppConsts.AttributeHash},
                @{AppConsts.AttributeRotateMode},
                @{AppConsts.AttributeFlipMode},
                @{AppConsts.AttributeLastView},
                @{AppConsts.AttributeHistory},
                @{AppConsts.AttributeVector},
                @{AppConsts.AttributeRate},
                @{AppConsts.AttributeDistance}
            );";
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", img.Hash);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeRotateMode}", (int)img.RotateMode);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeFlipMode}", (int)img.FlipMode);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastView}", img.LastView.Ticks);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHistory}", img.History);
            var vectorBytes = MemoryMarshal.Cast<float, byte>(GetVector(img.Hash)).ToArray();
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVector}", vectorBytes);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeRate}", img.Rate);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeDistance}", img.Distance);
            sqlCommand.ExecuteNonQuery();

            MaxImages--;
        }
    }

    public Img GetImgFromDatabase(string hash)
    {
        lock (_lock) {
            var sql = $@"
            SELECT
                {AppConsts.AttributeHash},
                {AppConsts.AttributeRotateMode},
                {AppConsts.AttributeFlipMode},
                {AppConsts.AttributeLastView},
                {AppConsts.AttributeHistory},
                {AppConsts.AttributeRate},
                {AppConsts.AttributeDistance}
            FROM {AppConsts.TableImages}
            WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash};";
            using var sqlCommand = new SqliteCommand(sql, _sqlConnection);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
            using var reader = sqlCommand.ExecuteReader();
            if (reader.Read()) {
                return new Img(
                    hash: reader.GetString(0),
                    rotateMode: Enum.Parse<RotateMode>(reader.GetInt64(1).ToString()),
                    flipMode: Enum.Parse<FlipMode>(reader.GetInt64(2).ToString()),
                    lastView: new DateTime(reader.GetInt64(3)),
                    history: reader.GetString(4),
                    rate: reader.GetInt32(5),
                    distance: reader.GetFloat(6),
                    images: this);
            }

            return new Img(
                hash: string.Empty,
                rotateMode: RotateMode.None,
                flipMode: FlipMode.None,
                lastView: DateTime.MinValue,
                history: string.Empty,
                rate: 0,
                distance: 0.0f,
                images: this);
        }
    }

    public void DeleteImgInDatabase(string hash)
    {
        lock (_lock) {
            RemoveVector(hash);
            using var command = new SqliteCommand(
                $"DELETE FROM {AppConsts.TableImages} WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}",
                _sqlConnection);
            command.Parameters.Clear();
            command.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
            command.ExecuteNonQuery();
        }
    }

    public int GetMinHistoryCount()
    {
        lock (_lock) {
            using var command = new SqliteCommand(
                $@"SELECT COUNT(*) FROM {AppConsts.TableImages} WHERE LENGTH({AppConsts.AttributeHistory}) = (SELECT MIN(LENGTH({AppConsts.AttributeHistory})) FROM {AppConsts.TableImages});",
                _sqlConnection);
            var result = command.ExecuteScalar();
            return Convert.ToInt32(result);
        }
    }

    /// <summary>
    /// Picks the subject with the smallest history-penalised distance:
    /// score = distance ^ (1 / (1 + HistoryPenaltyRate * entries)).
    /// Since every distance is in (0, 1), taking a root of it moves it towards 1, so the
    /// more history a row carries the worse its distance looks and the later it is picked,
    /// while the ordering inside one history length is the plain distance ordering.
    /// A row whose distance was never computed sorts as 0 - it is shown first so GetNext
    /// fills the column in.
    /// The penalty is a root rather than an added term on purpose: distance + rate * n
    /// leaves the (0, 1) range at a large n - with rate 0.1 a row with seven entries
    /// already scores 0.99 and anything above that is off the scale - while a root of a
    /// value below 1 stays below 1 no matter how much history piles up.
    /// </summary>
    public string PickNextSubject()
    {
        lock (_lock) {
            // POW() needs SQLite 3.35+ built with SQLITE_ENABLE_MATH_FUNCTIONS; the
            // bundled Microsoft.Data.Sqlite provider has it (verified against this build).
            // History is a concatenation of fixed-length hashes, so LENGTH()/HashLength is
            // the entry count - integer division, both operands being integers.
            using var command = new SqliteCommand(
                $@"SELECT {AppConsts.AttributeHash} FROM {AppConsts.TableImages}
                   ORDER BY POW(
                       COALESCE({AppConsts.AttributeDistance}, 0.0),
                       1.0 / (1.0 + {AppConsts.HistoryPenaltyRate} * (LENGTH({AppConsts.AttributeHistory}) / {AppConsts.HashLength})))
                   LIMIT 1;",
                _sqlConnection);
            return command.ExecuteScalar() as string ?? string.Empty;
        }
    }

    public DateTime GetLastView()
    {
        lock (_lock) {
            using var command = new SqliteCommand(
                $@"SELECT MIN({AppConsts.AttributeLastView}) FROM {AppConsts.TableImages};",
                _sqlConnection);
            var result = command.ExecuteScalar();
            var ticks = Convert.ToInt64(result);
            return new DateTime(ticks);
        }
    }

    public string? FindClosest(float[] vector)
    {
        lock (_lock) {
            // _hashToIndex holds only live slots, so free slots need no filtering.
            var minHistoryLength = int.MaxValue;
            foreach (var slot in _hashToIndex.Values) {
                if (_historyLength[slot] < minHistoryLength) {
                    minHistoryLength = _historyLength[slot];
                }
            }

            var slotArray = _hashToIndex.Values
                .Where(slot => _historyLength[slot] == minHistoryLength)
                .ToArray();
            if (slotArray.Length == 0) {
                return null;
            }

            var distances = new float[slotArray.Length];
            Parallel.For(0, slotArray.Length, i => {
                var v = _vectors.AsSpan(slotArray[i] * AppConsts.VectorSize, AppConsts.VectorSize);
                distances[i] = Vit.ComputeDistance(vector, v);
            });

            var best = 0;
            for (var i = 1; i < distances.Length; i++) {
                if (distances[i] < distances[best]) {
                    best = i;
                }
            }

            return _slotToHash[slotArray[best]];
        }
    }
}
