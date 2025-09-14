using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace ImgMzx;

public static class AppDatabase
{
    private static readonly object _lock = new();
    private static SqliteConnection _sqlConnection = new();
    private static Dictionary<string, float[]> _vectors = new();

    public static void Load(string filedatabase, IProgress<string>? progress, out int maxImages)
    {       
        lock (_lock) {
            var connectionString = $"Data Source={filedatabase};";
            _sqlConnection = new SqliteConnection(connectionString);
            _sqlConnection.Open();

            using (var pragmaCommand = _sqlConnection.CreateCommand()) {
                pragmaCommand.CommandText = "PRAGMA foreign_keys = ON";
                pragmaCommand.ExecuteNonQuery();
            }

            LoadVars(progress, out maxImages);
            LoadVectors(progress, maxImages, out _vectors);
        }
    }

    private static void LoadVars(
        IProgress<string>? progress,
        out int maxImages)
    {
        maxImages = 0;
        progress?.Report($"Loading vars{AppConsts.CharEllipsis}");
        var sb = new StringBuilder();
        sb.Append("SELECT ");
        sb.Append($"{AppConsts.AttributeMaxImages}"); // 0
        sb.Append($" FROM {AppConsts.TableVars};");
        using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
        using var reader = sqlCommand.ExecuteReader();
        if (reader.HasRows) {
            while (reader.Read()) {
                maxImages = reader.GetInt32(0);
                break;
            }
        }
    }

    private static void LoadVectors(
        IProgress<string>? progress,
        int maxImages,
        out Dictionary<string, float[]> vectors)
    {
        vectors = new Dictionary<string, float[]>(maxImages, StringComparer.Ordinal);
        var sb = new StringBuilder();
        sb.Append("SELECT ");
        sb.Append($"{AppConsts.AttributeHash},"); // 0
        sb.Append($"{AppConsts.AttributeVector}"); // 1
        sb.Append($" FROM {AppConsts.TableImages};");
        using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
        using var reader = sqlCommand.ExecuteReader();
        if (reader.HasRows) {
            var dtn = DateTime.Now;
            while (reader.Read()) {
                var hash = reader.GetString(0);
                var bytebuffer = (byte[])reader[1];
                var vector = AppVars.PoolFloat.Rent(AppConsts.VectorSize);
                Buffer.BlockCopy(bytebuffer, 0, vector, 0, AppConsts.VectorSize * sizeof(float));
                vectors[hash] = vector;

                if (!(DateTime.Now.Subtract(dtn).TotalMilliseconds > AppConsts.TimeLapse)) {
                    continue;
                }

                dtn = DateTime.Now;
                var count = vectors.Count;
                progress?.Report($"Loading vectors ({count}){AppConsts.CharEllipsis}");
            }
        }
    }

    public static Img? GetImg(string hash)
    {
        var sb = new StringBuilder();
        sb.Append("SELECT ");
        sb.Append($"{AppConsts.AttributeRotateMode},"); // 0
        sb.Append($"{AppConsts.AttributeFlipMode},"); // 1
        sb.Append($"{AppConsts.AttributeLastView},"); // 2
        sb.Append($"{AppConsts.AttributeNext},"); // 3
        sb.Append($"{AppConsts.AttributeScore},"); // 4
        sb.Append($"{AppConsts.AttributeLastCheck},"); // 5
        sb.Append($"{AppConsts.AttributeDistance}"); // 6
        sb.Append($" FROM {AppConsts.TableImages}");
        sb.Append($" WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}");
        using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
        sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
        using var reader = sqlCommand.ExecuteReader();
        if (reader.HasRows) {
            while (reader.Read()) {
                var rotatemode = (RotateMode)Enum.Parse(typeof(RotateMode), reader.GetInt64(0).ToString());
                var flipmode = (FlipMode)Enum.Parse(typeof(FlipMode), reader.GetInt64(1).ToString());
                var lastview = DateTime.FromBinary(reader.GetInt64(2));
                var next = reader.GetString(3);
                var score = (int)reader.GetInt64(4);
                var lastcheck = DateTime.FromBinary(reader.GetInt64(5));
                var distance = reader.GetFloat(6);

                var img = new Img();
                img.RotateMode = rotatemode;
                img.FlipMode = flipmode;
                img.LastView = lastview;
                img.Score = score;
                img.LastCheck = lastcheck;
                img.Next = next;
                img.Distance = distance;

                return img;
            }
        }

        return null;
    }

    public static void Add(string hash, Img img, float[] vector)
    {
        lock (_lock) {
            using (var sqlCommand = _sqlConnection.CreateCommand()) {
                sqlCommand.Connection = _sqlConnection;
                var sb = new StringBuilder();
                sb.Append($"INSERT INTO {AppConsts.TableImages} (");
                sb.Append($"{AppConsts.AttributeHash},");
                sb.Append($"{AppConsts.AttributeVector},");
                sb.Append($"{AppConsts.AttributeRotateMode},");
                sb.Append($"{AppConsts.AttributeFlipMode},");
                sb.Append($"{AppConsts.AttributeLastView},");
                sb.Append($"{AppConsts.AttributeNext},");
                sb.Append($"{AppConsts.AttributeScore},");
                sb.Append($"{AppConsts.AttributeLastCheck},");
                sb.Append($"{AppConsts.AttributeDistance}");
                sb.Append(") VALUES (");
                sb.Append($"@{AppConsts.AttributeHash},");
                sb.Append($"@{AppConsts.AttributeVector},");
                sb.Append($"@{AppConsts.AttributeRotateMode},");
                sb.Append($"@{AppConsts.AttributeFlipMode},");
                sb.Append($"@{AppConsts.AttributeLastView},");
                sb.Append($"@{AppConsts.AttributeNext},");
                sb.Append($"@{AppConsts.AttributeScore},");
                sb.Append($"@{AppConsts.AttributeLastCheck},");
                sb.Append($"@{AppConsts.AttributeDistance}");
                sb.Append(')');
                sqlCommand.CommandText = sb.ToString();
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVector}", Helper.ArrayFromFloat(vector));
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeRotateMode}", (int)img.RotateMode);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeFlipMode}", (int)img.FlipMode);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastView}", img.LastView.Ticks);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeNext}", img.Next);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeScore}", img.Score);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastCheck}", img.LastCheck.Ticks);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeDistance}", img.Distance);
                sqlCommand.ExecuteNonQuery();
            }

            _vectors[hash] = vector;
        }
    }

    public static void Delete(string hash)
    {
        lock (_lock) {
            if (_vectors.Remove(hash, out float[]? value)) {
                AppVars.PoolFloat.Return(value);
            }

            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.Connection = _sqlConnection;
            sqlCommand.CommandText =
                $"DELETE FROM {AppConsts.TableImages} WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}";
            sqlCommand.Parameters.Clear();
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
            sqlCommand.ExecuteNonQuery();
        }
    }

    public static void DeletePair(string hash)
    {
        lock (_lock) {
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.Connection = _sqlConnection;
            sqlCommand.CommandText =
                $"DELETE FROM {AppConsts.TablePairs} WHERE {AppConsts.AttributeHash1} = @{AppConsts.AttributeHash} OR {AppConsts.AttributeHash2} = @{AppConsts.AttributeHash}";
            sqlCommand.Parameters.Clear();
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
            sqlCommand.ExecuteNonQuery();
        }
    }

    public static void ImgUpdateProperty(string hash, string key, object val)
    {
        lock (_lock) {
            using (var sqlCommand = _sqlConnection.CreateCommand()) {
                sqlCommand.Connection = _sqlConnection;
                sqlCommand.CommandText =
                    $"UPDATE {AppConsts.TableImages} SET {key} = @{key} WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}";
                sqlCommand.Parameters.AddWithValue($"@{key}", val);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
                sqlCommand.ExecuteNonQuery();
            }
        }
    }

    public static void UpdateMaxImages()
    {
        lock (_lock) {
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.Connection = _sqlConnection;
            sqlCommand.CommandText =
                $"UPDATE {AppConsts.TableVars} SET {AppConsts.AttributeMaxImages} = @{AppConsts.AttributeMaxImages}";
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeMaxImages}", AppVars.MaxImages);
            sqlCommand.ExecuteNonQuery();
        }
    }

    public static int Count()
    {
        lock (_lock) {
            return _vectors.Count;
        }
    }

    public static bool ContainsKey(string hash)
    {
        lock (_lock) {
            return _vectors.ContainsKey(hash);
        }
    }

    public static Tuple<string, float>[] GetBeam(string hash)
    {
        Tuple<string, float>[] beam;
        lock (_lock) {
            beam = new Tuple<string, float>[_vectors.Count];
            var distances = new float[_vectors.Count];

            var vectorArray = new float[_vectors.Count][];
            var hashArray = new string[_vectors.Count];
            var index = 0;

            foreach (var kvp in _vectors) {
                hashArray[index] = kvp.Key;
                vectorArray[index] = kvp.Value;
                index++;
            }

            var vx = _vectors[hash];
            Parallel.For(0, distances.Length, i => {
                distances[i] = AppVit.GetDistance(vx, vectorArray[i]);
            });

            for (var i = 0; i < _vectors.Count; i++) {
                beam[i] = Tuple.Create(hashArray[i], distances[i]);
            }
        }

        return beam.OrderBy(e => e.Item2).ToArray();
    }

    public static DateTime GetMinimalLastView()
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
                    return DateTime.FromBinary(minLastViewTicks).AddSeconds(-1);
                }
            }

            return new DateTime(1980, 1, 1);
        }
    }

    public static string GetLastCheck()
    {
        lock (_lock) {
            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append($"{AppConsts.AttributeHash} ");
            sb.Append($"FROM {AppConsts.TableImages} ");
            sb.Append($"ORDER BY {AppConsts.AttributeLastCheck} ASC ");
            sb.Append("LIMIT 1;");

            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            using var reader = sqlCommand.ExecuteReader();

            if (reader.HasRows && reader.Read()) {
                return reader.GetString(0);
            }

            return string.Empty;
        }
    }

    public static HashSet<string> GetHistory(string hash)
    {
        lock (_lock) {
            var history = new HashSet<string>();
            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append($"{AppConsts.AttributeHash2} as paired_hash");
            sb.Append($" FROM {AppConsts.TablePairs}");
            sb.Append($" WHERE {AppConsts.AttributeHash1} = @hash");
            sb.Append(" UNION ");
            sb.Append("SELECT ");
            sb.Append($"{AppConsts.AttributeHash1} as paired_hash");
            sb.Append($" FROM {AppConsts.TablePairs}");
            sb.Append($" WHERE {AppConsts.AttributeHash2} = @hash;");

            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            sqlCommand.Parameters.AddWithValue("@hash", hash);
            using var reader = sqlCommand.ExecuteReader();

            if (reader.HasRows) {
                while (reader.Read()) {
                    history.Add(reader.GetString(0));
                }
            }

            return history;
        }
    }

    public static string GetXByLastView(IProgress<string>? progress)
    {
        lock (_lock) {
            var sb1 = new StringBuilder();
            sb1.Append("SELECT ");
            sb1.Append($"(MAX({AppConsts.AttributeLastView}) - MIN({AppConsts.AttributeLastView})) / 2 as e ");
            sb1.Append($"FROM {AppConsts.TableImages};");

            long e;
            using (var cmd1 = new SqliteCommand(sb1.ToString(), _sqlConnection))
            using (var reader1 = cmd1.ExecuteReader()) {
                if (!reader1.HasRows || !reader1.Read()) {
                    return string.Empty;
                }
                e = Math.Max(1000000, reader1.GetInt64(0));
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

            return string.Empty;
        }
    }

    public static string GetXByDistance(IProgress<string>? progress)
    {
        lock (_lock) {
            var sb2 = new StringBuilder();
            sb2.Append($"SELECT {AppConsts.AttributeHash} ");
            sb2.Append($"FROM {AppConsts.TableImages} ");
            sb2.Append($"ORDER BY {AppConsts.AttributeDistance} ");
            sb2.Append($"LIMIT 1;");

            using var cmd2 = new SqliteCommand(sb2.ToString(), _sqlConnection);
            using var reader2 = cmd2.ExecuteReader();

            if (reader2.HasRows && reader2.Read()) {
                return reader2.GetString(0);
            }

            return string.Empty;
        }
    }


    public static string GetX(IProgress<string>? progress)
    {
        var r = Random.Shared.Next(0, 100);
        if (r < 80) {
            return GetXByLastView(progress);
        } else {
            return GetXByDistance(progress);
        }
    }

    public static (string, string) GetNext(string hashU, string? hashD = null)
    {
        string message;
        var oldDistance = GetImg(hashU)!.Value.Distance;
        var history = GetHistory(hashU);
        var oldHistorySize = history.Count();
        if (!string.IsNullOrEmpty(hashD)) {
            ImgMdf.Delete(hashD);
        }

        var next = string.Empty;
        var distance = 2f;
        var beam = GetBeam(hashU);
        for (var i = 0; i < beam.Length; i++) {
            next = beam[i].Item1;
            if (next.Equals(hashU)) {
                continue;
            }

            if (history.Contains(next)) {
                continue;
            }

            lock (_lock) {
                if (!_vectors.TryGetValue(hashU, out var vecX) || !_vectors.TryGetValue(next, out var vecY)) {
                    continue;
                }

                distance = AppVit.GetDistance(vecX, vecY);
            }

            break;
        }

        var historySize = GetHistory(hashU).Count();
        var imgY = GetImg(next);
        if (Math.Abs(oldDistance - distance) >= 0.0001f) {
            if (oldHistorySize != historySize) {
                message = $"[{oldHistorySize}] {oldDistance:F4} {AppConsts.CharRightArrow} [{historySize}] {distance:F4}";
            }
            else {
                message = $"[{oldHistorySize}] {oldDistance:F4} {AppConsts.CharRightArrow} {distance:F4}";
            }
        }
        else {
            if (oldHistorySize != historySize) {
                message = $"[{oldHistorySize}] {AppConsts.CharRightArrow} [{historySize}] {distance:F4}";
            }
            else {
                message = $"[{historySize}] {distance:F4}";
            }
        }

        ImgUpdateProperty(hashU, AppConsts.AttributeNext, next);
        ImgUpdateProperty(hashU, AppConsts.AttributeDistance, distance);
        ImgUpdateProperty(hashU, AppConsts.AttributeLastCheck, DateTime.Now.Ticks);
        return (next, message);
    }

    public static string GetY(string hashX, IProgress<string>? progress)
    {
        progress?.Report($"Calculating{AppConsts.CharEllipsis}");
        var hashC = GetLastCheck();
        (var next, var message) = GetNext(hashC);
        progress?.Report(message);

        var sb = new StringBuilder();
        var count = Count();
        var diff = count - AppVars.MaxImages;
        sb.Append($"{count} ({diff})");
        
        (next, message) = GetNext(hashX);
        sb.Append($" {message}");
        progress?.Report(sb.ToString());
        return next;
    }

    public static void AddPair(string hash1, string hash2)
    {
        if (string.CompareOrdinal(hash1, hash2) > 0) {
            (hash1, hash2) = (hash2, hash1);
        }

        lock (_lock) {
            using (var sqlCommand = _sqlConnection.CreateCommand()) {
                sqlCommand.Connection = _sqlConnection;
                var sb = new StringBuilder();
                sb.Append($"INSERT OR IGNORE INTO {AppConsts.TablePairs} (");
                sb.Append($"{AppConsts.AttributeHash1},");
                sb.Append($"{AppConsts.AttributeHash2}");
                sb.Append(") VALUES (");
                sb.Append($"@{AppConsts.AttributeHash1},");
                sb.Append($"@{AppConsts.AttributeHash2}");
                sb.Append(')');
                sqlCommand.CommandText = sb.ToString();
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash1}", hash1);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash2}", hash2);
                sqlCommand.ExecuteNonQuery();
            }
        }
    }
}