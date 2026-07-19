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
            AddVector(img.Hash, vector);
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.CommandText = $@"
            INSERT INTO {AppConsts.TableImages} (
                {AppConsts.AttributeHash},
                {AppConsts.AttributeRotateMode},
                {AppConsts.AttributeFlipMode},
                {AppConsts.AttributeLastView},
                {AppConsts.AttributeHistory},
                {AppConsts.AttributeVector}
            ) VALUES (
                @{AppConsts.AttributeHash},
                @{AppConsts.AttributeRotateMode},
                @{AppConsts.AttributeFlipMode},
                @{AppConsts.AttributeLastView},
                @{AppConsts.AttributeHistory},
                @{AppConsts.AttributeVector}
            );";
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", img.Hash);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeRotateMode}", (int)img.RotateMode);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeFlipMode}", (int)img.FlipMode);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastView}", img.LastView.Ticks);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHistory}", img.History);
            var vectorBytes = MemoryMarshal.Cast<float, byte>(GetVector(img.Hash)).ToArray();
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVector}", vectorBytes);
            sqlCommand.ExecuteNonQuery();
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
                {AppConsts.AttributeHistory}
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
                    images: this);
            }

            return new Img(
                hash: string.Empty,
                rotateMode: RotateMode.None,
                flipMode: FlipMode.None,
                lastView: DateTime.MinValue,
                history: string.Empty,
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

    public int GetMinHistory()
    {
        lock (_lock) {
            using var command = new SqliteCommand(
                $@"SELECT MIN(LENGTH({AppConsts.AttributeHistory})) FROM {AppConsts.TableImages};",
                _sqlConnection);
            var result = command.ExecuteScalar();
            var historylength = Convert.ToInt32(result);
            return historylength / AppConsts.HashLength;
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

    public string GetHashLastView()
    {
        lock (_lock) {
            var sql = $@"
                WITH
                half AS (
                    SELECT {AppConsts.AttributeHash}, {AppConsts.AttributeHistory}
                    FROM {AppConsts.TableImages}
                    ORDER BY {AppConsts.AttributeLastView} ASC
                    LIMIT (SELECT COUNT(*) / 10 FROM {AppConsts.TableImages})
                ),
                grouped AS (
                    SELECT {AppConsts.AttributeHash},
                           LENGTH({AppConsts.AttributeHistory}) AS hlen,
                           ROW_NUMBER() OVER (
                               PARTITION BY LENGTH({AppConsts.AttributeHistory})
                               ORDER BY RANDOM()
                           ) AS rn
                    FROM half
                )
                SELECT {AppConsts.AttributeHash}
                FROM grouped
                WHERE rn = 1
                ORDER BY RANDOM()
                LIMIT 1";

            using var cmd = new SqliteCommand(sql, _sqlConnection);
            var result = cmd.ExecuteScalar();
            return result?.ToString() ?? string.Empty;
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
            var freeSlots = new HashSet<int>(_freeSlots);
            var hashArray = _hashToIndex.Keys
                .Where(h => !freeSlots.Contains(_hashToIndex[h]))
                .ToArray();
            if (hashArray.Length == 0) {
                return null;
            }

            var results = new (string Hash, float Distance)[hashArray.Length];
            Parallel.For(0, hashArray.Length, i => {
                var hash = hashArray[i];
                var slot = _hashToIndex[hash];
                var v = _vectors.AsSpan(slot * AppConsts.VectorSize, AppConsts.VectorSize);
                results[i] = (hash, Vit.ComputeDistance(vector, v));
            });

            return results.MinBy(r => r.Distance).Hash;
        }
    }
}
