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
            UpdateImgInDatabaseUnlocked(hash, key, val);
        }
    }

    private void UpdateImgInDatabaseUnlocked(string hash, string key, object val)
    {
        if (_hashToIndex.TryGetValue(hash, out var cachedSlot)) {
            if (key == AppConsts.AttributeHistory) {
                _historyLength[cachedSlot] = ((string?)val)?.Length ?? 0;
            }
        }

        using var sqlCommand = new SqliteCommand(
            $"UPDATE {AppConsts.TableImages} SET {key} = @value WHERE {AppConsts.AttributeHash} = @hash",
            _sqlConnection);
        sqlCommand.Parameters.AddWithValue("@value", val ?? DBNull.Value);
        sqlCommand.Parameters.AddWithValue("@hash", hash);
        sqlCommand.ExecuteNonQuery();
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
                {AppConsts.AttributeAge}
            ) VALUES (
                @{AppConsts.AttributeHash},
                @{AppConsts.AttributeRotateMode},
                @{AppConsts.AttributeFlipMode},
                @{AppConsts.AttributeLastView},
                @{AppConsts.AttributeHistory},
                @{AppConsts.AttributeVector},
                @{AppConsts.AttributeRate},
                @{AppConsts.AttributeAge}
            );";
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", img.Hash);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeRotateMode}", (int)img.RotateMode);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeFlipMode}", (int)img.FlipMode);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastView}", img.LastView.Ticks);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHistory}", img.History);
            var vectorBytes = MemoryMarshal.Cast<float, byte>(GetVector(img.Hash)).ToArray();
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVector}", vectorBytes);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeRate}", img.Rate);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeAge}", img.Age);
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
                {AppConsts.AttributeAge}
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
                    age: reader.GetFloat(6),
                    images: this);
            }

            return new Img(
                hash: string.Empty,
                rotateMode: RotateMode.None,
                flipMode: FlipMode.None,
                lastView: DateTime.MinValue,
                history: string.Empty,
                rate: 0,
                age: 0.0f,
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

    public string PickNextSubject()
    {
        lock (_lock) {
            using var command = new SqliteCommand(
                $@"SELECT {AppConsts.AttributeHash} FROM {AppConsts.TableImages}
                   ORDER BY {AppConsts.AttributeAge}
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
