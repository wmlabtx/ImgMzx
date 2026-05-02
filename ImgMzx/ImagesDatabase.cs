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
                {AppConsts.AttributeNext},
                {AppConsts.AttributeScore},
                {AppConsts.AttributeLastCheck},
                {AppConsts.AttributeDistance},
                {AppConsts.AttributeFamily},
                {AppConsts.AttributeFlag},
                {AppConsts.AttributeVector}
            ) VALUES (
                @{AppConsts.AttributeHash},
                @{AppConsts.AttributeRotateMode},
                @{AppConsts.AttributeFlipMode},
                @{AppConsts.AttributeLastView},
                @{AppConsts.AttributeNext},
                @{AppConsts.AttributeScore},
                @{AppConsts.AttributeLastCheck},
                @{AppConsts.AttributeDistance},
                @{AppConsts.AttributeFamily},
                @{AppConsts.AttributeFlag},
                @{AppConsts.AttributeVector}
            );";
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", img.Hash);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeRotateMode}", (int)img.RotateMode);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeFlipMode}", (int)img.FlipMode);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastView}", img.LastView.Ticks);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeNext}", img.Next);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeScore}", img.Score);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastCheck}", img.LastCheck.Ticks);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeDistance}", img.Distance);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeFamily}", img.Family);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeFlag}", img.Flag);
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
                {AppConsts.AttributeNext},
                {AppConsts.AttributeScore},
                {AppConsts.AttributeLastCheck},
                {AppConsts.AttributeDistance},
                {AppConsts.AttributeFamily},
                {AppConsts.AttributeFlag}
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
                    next: reader.GetString(4),
                    score: (int)reader.GetInt64(5),
                    lastCheck: new DateTime(reader.GetInt64(6)),
                    distance: reader.GetFloat(7),
                    family: (int)reader.GetInt64(8),
                    flag: (int)reader.GetInt64(9),
                    images: this);
            }

            return new Img(
                hash: string.Empty,
                rotateMode: RotateMode.None,
                flipMode: FlipMode.None,
                lastView: DateTime.MinValue,
                next: string.Empty,
                score: 0,
                lastCheck: DateTime.MinValue,
                distance: 0,
                family: 0,
                flag: 0,
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

    public string GetHashLastCheck()
    {
        lock (_lock) {
            using var command = new SqliteCommand(
                $"SELECT {AppConsts.AttributeHash} FROM {AppConsts.TableImages} ORDER BY {AppConsts.AttributeLastCheck} ASC LIMIT 1;",
                _sqlConnection);
            var result = command.ExecuteScalar();
            return result?.ToString() ?? string.Empty;
        }
    }

    public string GetHashLastView()
    {
        lock (_lock) {
            var resetDone = false;
            while (true) {
                if (_viewPool.Count == 0) {
                    foreach (var h in GetHashesByFlag(0)) {
                        _viewPoolIndex[h] = _viewPool.Count;
                        _viewPool.Add(h);
                    }

                    if (_viewPool.Count == 0) {
                        if (resetDone) {
                            return string.Empty;
                        }

                        ResetAllFlags();
                        foreach (var h in GetAllHashes()) {
                            var img = GetImgFromDatabase(h);
                            img.ResetFlag();
                        }

                        resetDone = true;
                        continue;
                    }
                }

                var hash = PickBestFromViewPool();
                RemoveFromViewPool(hash);

                if (_hashToIndex.ContainsKey(hash)) {
                    UpdateImgInDatabase(hash, AppConsts.AttributeFlag, 1);
                    return hash;
                }
            }
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

    public DateTime GetLastCheck()
    {
        lock (_lock) {
            using var command = new SqliteCommand(
                $@"SELECT MIN({AppConsts.AttributeLastCheck}) FROM {AppConsts.TableImages};",
                _sqlConnection);
            var result = command.ExecuteScalar();
            var ticks = Convert.ToInt64(result);
            return new DateTime(ticks);
        }
    }

    public int GetAvailableFamilyFromDatabase()
    {
        lock (_lock) {
            using var cmd = _sqlConnection.CreateCommand();
            cmd.CommandText = $@"
                WITH used AS (
                    SELECT DISTINCT {AppConsts.AttributeFamily} AS f 
                    FROM {AppConsts.TableImages} 
                    WHERE {AppConsts.AttributeFamily} > 0
                ),
                seq AS (
                    SELECT 1 AS n
                    UNION ALL
                    SELECT n + 1 FROM seq WHERE n < (SELECT COALESCE(MAX(f), 0) + 1 FROM used)
                )
                SELECT MIN(n) FROM seq WHERE n NOT IN (SELECT f FROM used);";
            var minGap = cmd.ExecuteScalar();
            if (minGap == null || minGap == DBNull.Value) {
                return 1;
            }

            return Convert.ToInt32(minGap);
        }
    }

    public int GetFamilySizeFromDatabase(int family)
    {
        lock (_lock) {
            using var cmd = _sqlConnection.CreateCommand();
            cmd.CommandText = $@"
                SELECT COUNT(*)
                FROM {AppConsts.TableImages}
                WHERE {AppConsts.AttributeFamily} = @family;";
            cmd.Parameters.AddWithValue("@family", family);
            var countObj = cmd.ExecuteScalar();
            if (countObj == null || countObj == DBNull.Value) {
                return 0;
            }
            return Convert.ToInt32(countObj);
        }
    }

    private const int ViewPoolSampleSize = 50;

    private string PickBestFromViewPool()
    {
        /*
        var k = Math.Min(ViewPoolSampleSize, _viewPool.Count);
        var sample = new List<string>(k);
        for (var i = 0; i < k; i++) {
            sample.Add(_viewPool[Random.Shared.Next(_viewPool.Count)]);
        }

        var placeholders = string.Join(",", Enumerable.Range(0, sample.Count).Select(i => $"@h{i}"));
        using var cmd = new SqliteCommand(
            $@"SELECT i1.{AppConsts.AttributeHash}
               FROM {AppConsts.TableImages} i1
               JOIN {AppConsts.TableImages} i2 ON i1.{AppConsts.AttributeNext} = i2.{AppConsts.AttributeHash}
               WHERE i1.{AppConsts.AttributeHash} IN ({placeholders})
               ORDER BY i2.{AppConsts.AttributeScore} DESC
               LIMIT 1",
            _sqlConnection);
        for (var i = 0; i < sample.Count; i++) {
            cmd.Parameters.AddWithValue($"@h{i}", sample[i]);
        }

        var result = cmd.ExecuteScalar()?.ToString();
        if (!string.IsNullOrEmpty(result)) {
            return result;
        }
        */

        return _viewPool[Random.Shared.Next(_viewPool.Count)];
    }

    private List<string> GetHashesByFlag(int flag)
    {
        lock (_lock) {
            using var command = new SqliteCommand(
                $"SELECT {AppConsts.AttributeHash} FROM {AppConsts.TableImages} WHERE {AppConsts.AttributeFlag} = @flag",
                _sqlConnection);
            command.Parameters.AddWithValue("@flag", flag);
            using var reader = command.ExecuteReader();
            var hashes = new List<string>();
            while (reader.Read()) {
                hashes.Add(reader.GetString(0));
            }
            return hashes;
        }
    }

    private void ResetAllFlags()
    {
        lock (_lock) {
            using var command = new SqliteCommand(
                $"UPDATE {AppConsts.TableImages} SET {AppConsts.AttributeFlag} = 0",
                _sqlConnection);
            command.ExecuteNonQuery();
        }
    }
}
