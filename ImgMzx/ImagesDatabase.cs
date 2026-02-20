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
                $"SELECT {AppConsts.AttributeMaxImages} FROM {AppConsts.TableVars};", 
                _sqlConnection);
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    public bool ContainsImg(string hash)
    {
        lock (_lock) {
            using var command = new SqliteCommand(
                $"SELECT COUNT(*) FROM {AppConsts.TableImages};",
                _sqlConnection);
            command.Parameters.AddWithValue("@hash", hash);
            using var reader = command.ExecuteReader();
            return reader.Read();
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
                return new Img(
                    hash: reader.GetString(0),
                    rotateMode: Enum.Parse<RotateMode>(reader.GetInt64(1).ToString()),
                    flipMode: Enum.Parse<FlipMode>(reader.GetInt64(2).ToString()),
                    lastView: new DateTime(reader.GetInt64(3)),
                    next: reader.GetString(4),
                    score: (int)reader.GetInt64(5),
                    lastCheck: new DateTime(reader.GetInt64(6)),
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
            var sql = $@"
            SELECT {AppConsts.AttributeHash}
            FROM {AppConsts.TableImages}
            WHERE {AppConsts.AttributeLastView} <= (
                SELECT MIN({AppConsts.AttributeLastView}) + @daysTicks FROM {AppConsts.TableImages}
            )
            ORDER BY RANDOM()
            LIMIT 1;";
            using var command = new SqliteCommand(sql, _sqlConnection);
            command.Parameters.AddWithValue("@daysTicks", TimeSpan.FromDays(365).Ticks);

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
            return new DateTime(Convert.ToInt64(result));
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
}
