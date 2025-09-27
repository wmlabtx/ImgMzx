using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Markup;
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

    private static void LoadVars(IProgress<string>? progress, out int maxImages)
    {
        maxImages = 0;
        progress?.Report($"Loading vars{AppConsts.CharEllipsis}");
        var sb = new StringBuilder();
        sb.Append("SELECT ");
        sb.Append($"{AppConsts.AttributeMaxImages}");
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

    private static void LoadVectors(IProgress<string>? progress, int maxImages, out Dictionary<string, float[]> vectors)
    {
        vectors = new Dictionary<string, float[]>(maxImages, StringComparer.Ordinal);
        var sb = new StringBuilder();
        sb.Append("SELECT ");
        sb.Append($"{AppConsts.AttributeHash},");
        sb.Append($"{AppConsts.AttributeVector}");
        sb.Append($" FROM {AppConsts.TableImages};");
        using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
        using var reader = sqlCommand.ExecuteReader();
        if (reader.HasRows) {
            var dtn = DateTime.Now;
            while (reader.Read()) {
                var hash = reader.GetString(0);
                var bytebuffer = (byte[])reader[1];
                var vector = Helper.ArrayToFloat(bytebuffer);
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
        sb.Append($"{AppConsts.AttributeDistance},"); // 6
        sb.Append($"{AppConsts.AttributeHash32}"); // 7
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
                var hash32 = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);

                var img = new Img();
                img.Hash = hash32;
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

    public static void Add(string hash, Img img, byte[] content, float[] vector)
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
                sb.Append($"{AppConsts.AttributeDistance},");
                sb.Append($"{AppConsts.AttributeContent}");
                sb.Append(") VALUES (");
                sb.Append($"@{AppConsts.AttributeHash},");
                sb.Append($"@{AppConsts.AttributeVector},");
                sb.Append($"@{AppConsts.AttributeRotateMode},");
                sb.Append($"@{AppConsts.AttributeFlipMode},");
                sb.Append($"@{AppConsts.AttributeLastView},");
                sb.Append($"@{AppConsts.AttributeNext},");
                sb.Append($"@{AppConsts.AttributeScore},");
                sb.Append($"@{AppConsts.AttributeLastCheck},");
                sb.Append($"@{AppConsts.AttributeDistance},");
                sb.Append($"@{AppConsts.AttributeContent}");
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
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeContent}", content);
                sqlCommand.ExecuteNonQuery();
            }

            _vectors[hash] = vector;
        }
    }

    public static void Delete(string hash)
    {
        lock (_lock) {
            _vectors.Remove(hash);

            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.Connection = _sqlConnection;
            sqlCommand.CommandText =
                $"DELETE FROM {AppConsts.TableImages} WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}";
            sqlCommand.Parameters.Clear();
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
            sqlCommand.ExecuteNonQuery();
        }
    }

    public static void ImgUpdateProperty(string hash, string key, object val)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new ArgumentException("Hash cannot be null or empty", nameof(hash));
        }
        
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or empty", nameof(key));
        }

        // Pre-validate column name to avoid SQL injection
        var validColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
        {
            AppConsts.AttributeVector,
            AppConsts.AttributeRotateMode,
            AppConsts.AttributeFlipMode,
            AppConsts.AttributeLastView,
            AppConsts.AttributeNext,
            AppConsts.AttributeScore,
            AppConsts.AttributeLastCheck,
            AppConsts.AttributeDistance,
            AppConsts.AttributeContent,
            AppConsts.AttributeHash32
        };

        if (!validColumns.Contains(key))
        {
            throw new ArgumentException($"Invalid column name: {key}", nameof(key));
        }

        lock (_lock) {
            try {
                using var sqlCommand = _sqlConnection.CreateCommand();
                
                // Simplified query without redundant Connection assignment
                sqlCommand.CommandText = $"UPDATE {AppConsts.TableImages} SET {key} = @value WHERE {AppConsts.AttributeHash} = @hash";
                
                sqlCommand.Parameters.AddWithValue("@value", val ?? DBNull.Value);
                sqlCommand.Parameters.AddWithValue("@hash", hash);
                
                var rowsAffected = sqlCommand.ExecuteNonQuery();
                
                if (rowsAffected == 0)
                {
                    throw new InvalidOperationException($"No record found with hash: {hash}");
                }
            }
            catch (Exception ex) when (!(ex is ArgumentException || ex is InvalidOperationException))
            {
                throw new InvalidOperationException($"Failed to update property {key} for hash {hash}: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Optimized method for updating LastCheck timestamp with minimal overhead
    /// </summary>
    public static void UpdateLastCheck(string hash)
    {
        lock (_lock) {
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.CommandText = $"UPDATE {AppConsts.TableImages} SET {AppConsts.AttributeLastCheck} = @ticks WHERE {AppConsts.AttributeHash} = @hash";
            sqlCommand.Parameters.AddWithValue("@ticks", DateTime.Now.Ticks);
            sqlCommand.Parameters.AddWithValue("@hash", hash);
            sqlCommand.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Optimized method for updating multiple properties in a single transaction
    /// </summary>
    public static void UpdateImageProperties(string hash, string next, float distance, long lastCheckTicks)
    {
        lock (_lock) {
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.CommandText = $"UPDATE {AppConsts.TableImages} SET {AppConsts.AttributeNext} = @next, {AppConsts.AttributeDistance} = @distance, {AppConsts.AttributeLastCheck} = @lastcheck WHERE {AppConsts.AttributeHash} = @hash";
            sqlCommand.Parameters.AddWithValue("@next", next);
            sqlCommand.Parameters.AddWithValue("@distance", distance);
            sqlCommand.Parameters.AddWithValue("@lastcheck", lastCheckTicks);
            sqlCommand.Parameters.AddWithValue("@hash", hash);
            sqlCommand.ExecuteNonQuery();
        }
    }

    public static void ImgWriteContent(string hash, byte[] imagedata)
    {
        var encryptedArray = AppEncryption.Encrypt(imagedata, hash);
        lock (_lock) {
            using (var sqlCommand = _sqlConnection.CreateCommand()) {
                sqlCommand.Connection = _sqlConnection;
                sqlCommand.CommandText =
                    $"UPDATE {AppConsts.TableImages} SET {AppConsts.AttributeContent} = @{AppConsts.AttributeContent} WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}";
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeContent}", encryptedArray);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
                sqlCommand.ExecuteNonQuery();
            }
        }
    }

    public static byte[] ImgReadContent(string hash)
    {
        var encryptedArray = Array.Empty<byte>();
        lock (_lock) {
            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append($"{AppConsts.AttributeContent}");
            sb.Append($" FROM {AppConsts.TableImages}");
            sb.Append($" WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash};");
            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
            using var reader = sqlCommand.ExecuteReader();
            if (reader.HasRows) {
                while (reader.Read()) {
                    if (!reader.IsDBNull(0)) {
                        encryptedArray = (byte[])reader[0];
                    }
                    break;
                }
            }
        }

        if (encryptedArray.Length == 0) {
            return encryptedArray;
        }

        var data = AppEncryption.Decrypt(encryptedArray, hash);
        return data ?? Array.Empty<byte>();
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

    public static float[] GetVector(string hash)
    {
        lock (_lock) {
            return _vectors[hash];
        }
    }

    public static void SetVector(string hash, float[] vector)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new ArgumentException("Hash cannot be null or empty", nameof(hash));
        }
        
        if (vector == null || vector.Length == 0)
        {
            throw new ArgumentException("Vector cannot be null or empty", nameof(vector));
        }

        lock (_lock) {
            try {
                _vectors[hash] = vector;

                var vectorBytes = Helper.ArrayFromFloat(vector);
                ImgUpdateProperty(hash, AppConsts.AttributeVector, vectorBytes);
            }
            catch (Exception ex)
            {
                _vectors.Remove(hash);
                Console.WriteLine($"Error in SetVector: hash={hash}, vectorLength={vector.Length}, exception={ex.Message}");
                throw;
            }
        }
    }

    public static Tuple<string, float>[] GetBeam(string hash)
    {
        lock (_lock) {
            var vectorCount = _vectors.Count;
            if (vectorCount == 0 || !_vectors.ContainsKey(hash)) {
                return Array.Empty<Tuple<string, float>>();
            }

            var results = new List<Tuple<string, float>>(vectorCount);
            var vx = _vectors[hash];

            var kvpArray = _vectors.ToArray();
            
            Parallel.ForEach(kvpArray, kvp => {
                if (kvp.Value != null) {
                    var distance = AppVit.GetDistance(vx, kvp.Value);
                    lock (results) {
                        results.Add(Tuple.Create(kvp.Key, distance));
                    }
                }
            });

            Array.Clear(kvpArray, 0, kvpArray.Length);

            return results.OrderBy(e => e.Item2).ToArray();
        }
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
        if (string.IsNullOrWhiteSpace(hashU))
        {
            return (string.Empty, "invalid hash");
        }

        string message;
        
        try {
            var imgU = GetImg(hashU);
            if (imgU == null)
            {
                return (string.Empty, "image not found");
            }
            
            var oldDistance = imgU.Value.Distance;
            
            if (!string.IsNullOrEmpty(hashD)) {
                Delete(hashD);
            }

            var vector = GetVector(hashU);
            if (vector.Length != AppConsts.VectorSize) {
                var imagedata = ImgReadContent(hashU);
                if (imagedata == null || imagedata.Length == 0) {
                    Delete(hashU);
                    return (string.Empty, "content not found");
                }

                using var image = AppBitmap.GetImage(imagedata);
                if (image == null) {
                    Delete(hashU);
                    return (string.Empty, "invalid image");
                }

                vector = AppVit.GetVector(image);
                SetVector(hashU, vector);
                
                imagedata = null;
                GC.Collect();
            }

            var next = string.Empty;
            var distance = 1f;
            var beam = GetBeam(hashU);
            
            try {
                for (var i = 0; i < beam.Length; i++) {
                    next = beam[i].Item1;
                    if (next.Equals(hashU)) {
                        continue;
                    }

                    distance = beam[i].Item2;
                    break;
                }
            }
            finally {
                if (beam != null && beam.Length > 0) {
                    Array.Clear(beam, 0, beam.Length);
                }
                beam = null;
            }

            if (string.IsNullOrEmpty(next))
            {
                return (string.Empty, "no suitable next image found");
            }

            if (Math.Abs(oldDistance - distance) >= 0.0001f) {
                message = $"{oldDistance:F4} {AppConsts.CharRightArrow} {distance:F4}";
            }
            else {
                message = $"{distance:F4}";
            }

            return (next, message);
        }
        catch (Exception ex) {
            Console.WriteLine($"Error in GetNext: hashU={hashU}, hashD={hashD}, exception={ex.Message}");
            return (string.Empty, $"error: {ex.Message}");
        }
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

    public static void ValidateDatabase(IProgress<string>? progress = null)
    {
        lock (_lock) {
            try {
                progress?.Report("Validating database integrity...");
                
                if (_sqlConnection.State != System.Data.ConnectionState.Open) {
                    throw new InvalidOperationException("Database connection is not open");
                }

                var dbHashes = new HashSet<string>();
                var sb = new StringBuilder();
                sb.Append($"SELECT {AppConsts.AttributeHash} FROM {AppConsts.TableImages}");
                
                using var cmd = new SqliteCommand(sb.ToString(), _sqlConnection);
                using var reader = cmd.ExecuteReader();
                
                while (reader.Read()) {
                    var hash = reader.GetString(0);
                    dbHashes.Add(hash);
                }

                var memoryHashes = new HashSet<string>(_vectors.Keys);
                var missingInMemory = dbHashes.Except(memoryHashes).ToList();
                var missingInDb = memoryHashes.Except(dbHashes).ToList();

                if (missingInMemory.Count > 0) {
                    progress?.Report($"Warning: {missingInMemory.Count} vectors missing in memory");
                    foreach (var hash in missingInMemory.Take(5)) {
                        Console.WriteLine($"Missing in memory: {hash}");
                    }
                }

                if (missingInDb.Count > 0) {
                    progress?.Report($"Warning: {missingInDb.Count} vectors missing in database");
                    foreach (var hash in missingInDb.Take(5)) {
                        Console.WriteLine($"Missing in DB: {hash}");
                        _vectors.Remove(hash);
                    }
                }

                progress?.Report($"Database validation complete. Memory: {_vectors.Count}, DB: {dbHashes.Count}");
            }
            catch (Exception ex) {
                progress?.Report($"Database validation failed: {ex.Message}");
                Console.WriteLine($"Database validation error: {ex}");
            }
        }
    }

    public static void DiagnoseImgUpdatePropertyError(string hash, string key, object val)
    {
        Console.WriteLine("=== ImgUpdateProperty Diagnostics ===");
        Console.WriteLine($"Hash: '{hash}' (length: {hash?.Length})");
        Console.WriteLine($"Key: '{key}' (length: {key?.Length})");
        Console.WriteLine($"Value: '{val}' (type: {val?.GetType().Name})");
        
        try {
            var img = GetImg(hash!);
            Console.WriteLine($"Record exists: {img != null}");
            if (img != null) {
                Console.WriteLine($"Record details: RotateMode={img.Value.RotateMode}, Score={img.Value.Score}");
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error checking record: {ex.Message}");
        }

        Console.WriteLine($"DB Connection State: {_sqlConnection?.State}");
        Console.WriteLine($"DB Connection String: {_sqlConnection?.ConnectionString}");
        
        Console.WriteLine("=== End Diagnostics ===");
    }
}