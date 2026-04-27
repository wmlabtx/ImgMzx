using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Processing;
using System.Runtime.InteropServices;
using System.Text;

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
                    next: reader.GetString(4),
                    score: (int)reader.GetInt64(5),
                    lastCheck: new DateTime(reader.GetInt64(6)),
                    distance: reader.GetFloat(7),
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
            /*
            var sql = $@"
                SELECT c.{AppConsts.AttributeHash}
                FROM {AppConsts.TableImages} c
                JOIN {AppConsts.TableImages} n ON c.{AppConsts.AttributeNext} = n.{AppConsts.AttributeHash}
                ORDER BY n.{AppConsts.AttributeScore} DESC, c.{AppConsts.AttributeLastView} ASC
                LIMIT 1;";
            */

            var sql = $@"
                SELECT {AppConsts.AttributeHash}
                FROM {AppConsts.TableImages}
                ORDER BY RANDOM()
                LIMIT 1;";


            using var cmd = new SqliteCommand(sql, _sqlConnection);
            using var reader = cmd.ExecuteReader();

            if (reader.HasRows && reader.Read()) {
                return reader.GetString(0);
            }

            return string.Empty;
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
            var sql = $@"
                SELECT
                    MIN(MAX(c.{AppConsts.AttributeLastView}, COALESCE(n.{AppConsts.AttributeLastView}, 0))),
                    COUNT(*)
                FROM {AppConsts.TableImages} c
                LEFT JOIN {AppConsts.TableImages} n ON n.{AppConsts.AttributeHash} = c.{AppConsts.AttributeNext}
                WHERE c.{AppConsts.AttributeScore} = (SELECT MIN({AppConsts.AttributeScore}) FROM {AppConsts.TableImages});";
            using var cmd = new SqliteCommand(sql, _sqlConnection);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return string.Empty;
            var minEffectiveTicks = reader.GetInt64(0);
            var count = reader.GetInt32(1);
            var daysOld = (int)(DateTime.Now - new DateTime(minEffectiveTicks)).TotalDays;
            return $"{daysOld}:{count}";
        }
    }
}
