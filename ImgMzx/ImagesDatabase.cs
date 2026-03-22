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

    private static readonly long _minValidTicks = new DateTime(1970, 1, 1).Ticks;

    private void FixTicksInDatabase(string hash, string column, long ticks)
    {
        using var sqlCommand = new SqliteCommand(
            $"UPDATE {AppConsts.TableImages} SET {column} = @ticks WHERE {AppConsts.AttributeHash} = @hash",
            _sqlConnection);
        sqlCommand.Parameters.AddWithValue("@ticks", ticks);
        sqlCommand.Parameters.AddWithValue("@hash", hash);
        sqlCommand.ExecuteNonQuery();
    }

    public void AddImgToDatabase(Img img, Span<float> vector)
    {
        if (img.LastView.Ticks > 0 && img.LastView.Ticks < _minValidTicks)
            throw new ArgumentException($"AddImg: LastView too old: {img.LastView} (ticks={img.LastView.Ticks})");
        if (img.LastCheck.Ticks > 0 && img.LastCheck.Ticks < _minValidTicks)
            throw new ArgumentException($"AddImg: LastCheck too old: {img.LastCheck} (ticks={img.LastCheck.Ticks})");

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
                {AppConsts.AttributeHistory},
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
                @{AppConsts.AttributeHistory},
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
            var vectorBytes = MemoryMarshal.Cast<float, byte>(GetVector(img.Hash)).ToArray();
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVector}", vectorBytes);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHistory}", img.History);
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
                {AppConsts.AttributeHistory}
            FROM {AppConsts.TableImages}
            WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash};";
            using var sqlCommand = new SqliteCommand(sql, _sqlConnection);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
            using var reader = sqlCommand.ExecuteReader();
            if (reader.Read()) {
                var hash0 = reader.GetString(0);
                var lastViewTicks = reader.GetInt64(3);
                var lastCheckTicks = reader.GetInt64(6);
                if (lastViewTicks < _minValidTicks) {
                    System.Diagnostics.Debug.WriteLine($"[FIX] lastview hash={hash0} ticks={lastViewTicks}");
                    lastViewTicks = DateTime.Now.Ticks;
                    FixTicksInDatabase(hash0, AppConsts.AttributeLastView, lastViewTicks);
                }
                if (lastCheckTicks < _minValidTicks) {
                    System.Diagnostics.Debug.WriteLine($"[FIX] lastcheck hash={hash0} ticks={lastCheckTicks}");
                    lastCheckTicks = new DateTime(1990, 1, 1).Ticks;
                    FixTicksInDatabase(hash0, AppConsts.AttributeLastCheck, lastCheckTicks);
                }
                return new Img(
                    hash: hash0,
                    rotateMode: Enum.Parse<RotateMode>(reader.GetInt64(1).ToString()),
                    flipMode: Enum.Parse<FlipMode>(reader.GetInt64(2).ToString()),
                    lastView: new DateTime(lastViewTicks),
                    next: reader.GetString(4),
                    score: (int)reader.GetInt64(5),
                    lastCheck: new DateTime(lastCheckTicks),
                    distance: reader.GetFloat(7),
                    history: reader.GetString(8),
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
            using var command = new SqliteCommand(
                $@"
            SELECT {AppConsts.AttributeHash}
            FROM {AppConsts.TableImages}
            WHERE LENGTH({AppConsts.AttributeHistory}) = (
                SELECT MIN(LENGTH({AppConsts.AttributeHistory}))
                FROM {AppConsts.TableImages}
            )
            AND {AppConsts.AttributeScore} = (
                SELECT MIN({AppConsts.AttributeScore})
                FROM {AppConsts.TableImages}
                WHERE LENGTH({AppConsts.AttributeHistory}) = (
                    SELECT MIN(LENGTH({AppConsts.AttributeHistory}))
                    FROM {AppConsts.TableImages}
                )
            )
            ORDER BY {AppConsts.AttributeDistance} DESC
            LIMIT 1;",
                _sqlConnection);

            var result = command.ExecuteScalar();
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
            return ticks >= _minValidTicks ? new DateTime(ticks) : DateTime.Now;
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
            return ticks >= _minValidTicks ? new DateTime(ticks) : new DateTime(1990, 1, 1);
        }
    }

    private string GetNearGroup()
    {
        lock (_lock) {
            var minLenSql = $@"
                SELECT MIN(LENGTH({AppConsts.AttributeHistory})) 
                FROM {AppConsts.TableImages};";
            using var minLenCmd = new SqliteCommand(minLenSql, _sqlConnection);
            var minLenChars = Convert.ToInt32(minLenCmd.ExecuteScalar());
            var minLen = minLenChars / AppConsts.HashLength;

            var minScoreSql = $@"
                SELECT MIN({AppConsts.AttributeScore})
                FROM {AppConsts.TableImages}
                WHERE LENGTH({AppConsts.AttributeHistory}) = @minLenChars;";
            using var minScoreCmd = new SqliteCommand(minScoreSql, _sqlConnection);
            minScoreCmd.Parameters.AddWithValue("@minLenChars", minLenChars);
            var minScore = Convert.ToInt32(minScoreCmd.ExecuteScalar());

            var groupLenSql = $@"
                SELECT COUNT(*)
                FROM {AppConsts.TableImages}
                WHERE LENGTH({AppConsts.AttributeHistory}) = @minLenChars
                  AND {AppConsts.AttributeScore} = @minScore;";
            using var groupLenCmd = new SqliteCommand(groupLenSql, _sqlConnection);
            groupLenCmd.Parameters.AddWithValue("@minLenChars", minLenChars);
            groupLenCmd.Parameters.AddWithValue("@minScore", minScore);
            var groupLen = Convert.ToInt32(groupLenCmd.ExecuteScalar());
            return $"{minLen}:{minScore}:{groupLen}";
        }
    }

    public void UpdateRecentInDatabase(int index, ReadOnlySpan<float> vector)
    {
        lock (_lock) {
            using var sqlCommand = new SqliteCommand(
                $"UPDATE {AppConsts.TableRecent} SET {AppConsts.AttributeVector} = @value WHERE [{AppConsts.AttributeIndex}] = @index",
                _sqlConnection);
            sqlCommand.Parameters.AddWithValue("@value", MemoryMarshal.Cast<float, byte>(vector).ToArray());
            sqlCommand.Parameters.AddWithValue("@index", index);
            sqlCommand.ExecuteNonQuery();
        }
    }
}
