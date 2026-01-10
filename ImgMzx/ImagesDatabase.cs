using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Processing;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text;

namespace ImgMzx;

public partial class Images : IDisposable
{
    public void LoadVectorsFromDatabase(IProgress<string>? progress)
    {
        var sb = new StringBuilder();
        sb.Append("SELECT ");
        sb.Append($"{AppConsts.AttributeHash}, ");
        sb.Append($"{AppConsts.AttributeVector} ");
        sb.Append($"FROM {AppConsts.TableImages};");

        using var command = new SqliteCommand(sb.ToString(), _sqlConnection);
        using var reader = command.ExecuteReader();

        var tempVectors = ArrayPool<float>.Shared.Rent(VectorSize);
        var dt = DateTime.Now;
        try {
            while (reader.Read()) {
                var hash = reader.GetString(0);
                var vectorBytes = (byte[])reader[1];

                if (vectorBytes.Length == VectorSize * sizeof(float)) {
                    var vectorSpan = MemoryMarshal.Cast<byte, float>(vectorBytes.AsSpan());
                    if (vectorSpan.Length == VectorSize) {
                        vectorSpan.CopyTo(tempVectors.AsSpan(0, VectorSize));
                        AddVector(hash, tempVectors.AsSpan(0, VectorSize));
                    }
                }

                if (DateTime.Now.Subtract(dt).TotalMilliseconds >= AppConsts.TimeLapse) {
                    dt = DateTime.Now;
                    progress?.Report($"Loaded {_countVectors} vectors...");
                }
            }
        }
        finally {
            ArrayPool<float>.Shared.Return(tempVectors);
        }
    }

    public void UpdateMaxImagesInDatabase(int maxImages)
    {
        lock (_lock) {
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.Connection = _sqlConnection;
            sqlCommand.CommandText =
                $"UPDATE {AppConsts.TableVars} SET {AppConsts.AttributeMaxImages} = @{AppConsts.AttributeMaxImages}";
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeMaxImages}", maxImages);
            sqlCommand.ExecuteNonQuery();
            _maxImages = maxImages;
        }
    }

    public int GetCountFromDatabase()
    {
        lock (_lock) {
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

    private string GetNearGroupFromDatabase()
    {
        lock (_lock) {
            using var cmdMin = _sqlConnection.CreateCommand();
            cmdMin.CommandText = $"SELECT MIN(LENGTH({AppConsts.AttributeHistory})) FROM {AppConsts.TableImages};";
            var minObj = cmdMin.ExecuteScalar();
            if (minObj == null || minObj == DBNull.Value) {
                return "0:0:0";
            }

            var histLen = Convert.ToInt32(minObj);

            using var cmdScore = _sqlConnection.CreateCommand();
            cmdScore.CommandText = $"SELECT MIN({AppConsts.AttributeScore}) FROM {AppConsts.TableImages} WHERE LENGTH({AppConsts.AttributeHistory}) = @minLen;";
            cmdScore.Parameters.AddWithValue("@minLen", histLen);
            var minScoreObj = cmdScore.ExecuteScalar();
            if (minScoreObj == null || minScoreObj == DBNull.Value) {
                return "0:0:0";
            }

            var score = Convert.ToInt32(minScoreObj);

            using var cmdCount = _sqlConnection.CreateCommand();
            cmdCount.CommandText = $"SELECT COUNT(*) FROM {AppConsts.TableImages} WHERE LENGTH({AppConsts.AttributeHistory}) = @minLen AND {AppConsts.AttributeScore} = @score;";
            cmdCount.Parameters.AddWithValue("@minLen", histLen);
            cmdCount.Parameters.AddWithValue("@score", score);
            var cntObj = cmdCount.ExecuteScalar();
            var cnt = cntObj == null || cntObj == DBNull.Value ? 0 : Convert.ToInt32(cntObj);

            var groupLen = histLen / AppConsts.HashLength;
            return $"{groupLen}:{score}:{cnt}";
        }
    }

    public Img? GetImgFromDatabase(string hash)
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
            sb.Append($"{AppConsts.AttributeHash},"); // 7
            sb.Append($"{AppConsts.AttributeHistory},"); // 8
            sb.Append($"{AppConsts.AttributeFamily}"); // 9
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
                        Hash = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                        History = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                        Family = reader.IsDBNull(9) ? 0 : (int)reader.GetInt64(9)
                    };

                    return img;
                }
            }

            return null;
        }
    }

    public bool ContainsImgInDatabase(string hash)
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

    public void AddImgToDatabase(Img img, float[] vector)
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
                sb.Append($"{AppConsts.AttributeHistory},");
                sb.Append($"{AppConsts.AttributeFamily}");
                sb.Append(") VALUES (");
                sb.Append($"@{AppConsts.AttributeHash},");
                sb.Append($"@{AppConsts.AttributeRotateMode},");
                sb.Append($"@{AppConsts.AttributeFlipMode},");
                sb.Append($"@{AppConsts.AttributeLastView},");
                sb.Append($"@{AppConsts.AttributeNext},");
                sb.Append($"@{AppConsts.AttributeScore},");
                sb.Append($"@{AppConsts.AttributeLastCheck},");
                sb.Append($"@{AppConsts.AttributeDistance},");
                sb.Append($"@{AppConsts.AttributeVector},");
                sb.Append($"@{AppConsts.AttributeHistory},");
                sb.Append($"@{AppConsts.AttributeFamily}");
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
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVector}", Helper.ArrayFromFloat(vector));
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHistory}", img.History);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeFamily}", img.Family);
                sqlCommand.ExecuteNonQuery();
            }
        }
    }

    public void UpdateImgInDatabase(string hash, string key, object val)
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

    public void DeleteImgInDatabase(string hash)
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

    public string? GetLastCheckFromDatabase()
    {
        lock (_lock) {
            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append($"{AppConsts.AttributeHash} ");
            sb.Append($"FROM {AppConsts.TableImages} ");
            sb.Append($"ORDER BY {AppConsts.AttributeLastCheck} ");
            sb.Append("LIMIT 1;");

            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            using var reader = sqlCommand.ExecuteReader();

            if (reader.HasRows && reader.Read()) {
                return reader.GetString(0);
            }

            return null;
        }
    }

    public DateTime? GetLastViewFromDatabase()
    {
        lock (_lock) {
            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append($"MIN({AppConsts.AttributeLastView}) ");
            sb.Append($"FROM {AppConsts.TableImages} ");

            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            using var reader = sqlCommand.ExecuteReader();

            if (reader.HasRows && reader.Read()) {
                return new DateTime(reader.GetInt64(0));
            }

            return null;
        }
    }

    private void UpdateLastHashInDatabase(string hash)
    {
        lock (_lock) {
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.Connection = _sqlConnection;
            sqlCommand.CommandText =
                $"UPDATE {AppConsts.TableVars} SET {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}";
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
            sqlCommand.ExecuteNonQuery();
        }
    }

    public string? GetXLastHashFromDatabase()
    {
        lock (_lock) {
            string? hash = null;
            using (var cmd = _sqlConnection.CreateCommand()) {
                cmd.CommandText = $"SELECT {AppConsts.AttributeHash} FROM {AppConsts.TableImages} WHERE {AppConsts.AttributeHash} > @hash ORDER BY {AppConsts.AttributeHash} LIMIT 1;";
                cmd.Parameters.AddWithValue("@hash", _lastHash);
                using var reader = cmd.ExecuteReader();
                if (reader.HasRows && reader.Read()) {
                    hash = reader.GetString(0);
                }
            }

            if (!string.IsNullOrEmpty(hash)) {
                _lastHash = hash;
                UpdateLastHashInDatabase(_lastHash);
                return hash;
            }

            using (var cmd = _sqlConnection.CreateCommand()) {
                cmd.CommandText = $"SELECT {AppConsts.AttributeHash} FROM {AppConsts.TableImages} ORDER BY {AppConsts.AttributeHash} LIMIT 1;";
                using var reader = cmd.ExecuteReader();
                if (reader.HasRows && reader.Read()) {
                    hash = reader.GetString(0);
                }
            }

            if (!string.IsNullOrEmpty(hash)) {
                _lastHash = hash;
                UpdateLastHashInDatabase(_lastHash);
                return hash;
            }

            return null;
        }
    }

    public string? GetXMinDistance()
    {
        lock (_lock) {
            using var cmd = _sqlConnection.CreateCommand();
            cmd.CommandText = $@"
                SELECT {AppConsts.AttributeHash}
                FROM {AppConsts.TableImages}
                ORDER BY LENGTH({AppConsts.AttributeHistory}), {AppConsts.AttributeScore}, {AppConsts.AttributeDistance}
                LIMIT 1;";
            using var reader = cmd.ExecuteReader();
            if (reader.HasRows && reader.Read()) {
                return reader.GetString(0);
            }

            return null;
        }
    }

    public string? GetXMinLastView()
    {
        lock (_lock) {
            using var cmd = _sqlConnection.CreateCommand();
            cmd.CommandText = $@"
                SELECT {AppConsts.AttributeHash}
                FROM {AppConsts.TableImages}
                ORDER BY {AppConsts.AttributeLastView}
                LIMIT 1;";
            using var reader = cmd.ExecuteReader();
            if (reader.HasRows && reader.Read()) {
                return reader.GetString(0);
            }

            return null;
        }
    }

    public string? GetXMinFamily()
    {
        lock (_lock) {
            using var cmd = _sqlConnection.CreateCommand();
            cmd.CommandText = $@"
                SELECT {AppConsts.AttributeHash}
                FROM {AppConsts.TableImages}
                WHERE {AppConsts.AttributeFamily} > 0
                ORDER BY {AppConsts.AttributeLastView}
                LIMIT 1;";
            using var reader = cmd.ExecuteReader();
            if (reader.HasRows && reader.Read()) {
                return reader.GetString(0);
            }

            return null;
        }
    }

    public string? GetX()
    {
        var imode = Random.Shared.Next(30);
        if (imode < 1) {
            return GetXLastHashFromDatabase();
        }

        if (imode < 2) {
            return GetXMinDistance();
        }

        if (imode < 3) {
            return GetXMinLastView(); ;
        }

        return GetXMinFamily();
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

    public void RenameFamilyInDatabase(int oldFamily, int newFamily)
    {
        lock (_lock) {
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.CommandText =
                $"UPDATE {AppConsts.TableImages} SET {AppConsts.AttributeFamily} = @newFamily, {AppConsts.AttributeLastCheck} = @lastCheck WHERE {AppConsts.AttributeFamily} = @oldFamily";
            sqlCommand.Parameters.AddWithValue("@newFamily", newFamily);
            sqlCommand.Parameters.AddWithValue("@oldFamily", oldFamily);
            sqlCommand.Parameters.AddWithValue("@lastCheck", DateTime.MinValue.Ticks);
            sqlCommand.ExecuteNonQuery();
        }
    }

    public void InvalidateFamiliyInDatabase(int family)
    {
        lock (_lock) {
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.CommandText =
                $"UPDATE {AppConsts.TableImages} SET {AppConsts.AttributeLastCheck} = @lastCheck WHERE {AppConsts.AttributeFamily} = @family";
            sqlCommand.Parameters.AddWithValue("@family", family);
            sqlCommand.Parameters.AddWithValue("@lastCheck", DateTime.MinValue.Ticks);
            sqlCommand.ExecuteNonQuery();
        }
    }
}
