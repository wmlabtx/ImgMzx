using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace ImgMzx;

public static class AppDatabase
{
    private static readonly object _lock = new();
    private static SqliteConnection _sqlConnection = new();

    public static void Load(
        string filedatabase, 
        IProgress<string>? progress, 
        out SortedList<string, Img> imgList, 
        out SortedList<string, string> nameList,
        out SortedList<int, DateTime> clusterList,
        out int maxImages)
    {       
        lock (_lock) {
            var connectionString = $"Data Source={filedatabase};";
            _sqlConnection = new SqliteConnection(connectionString);
            _sqlConnection.Open();

            LoadImages(progress, out imgList, out nameList);
            LoadVars(progress, out maxImages);
            LoadClusters(progress, out clusterList);
        }
    }

    private static void LoadImages(
        IProgress<string>? progress, 
        out SortedList<string, Img> imgList, 
        out SortedList<string, string> nameList)
    {
        imgList = new SortedList<string, Img>(StringComparer.OrdinalIgnoreCase);
        nameList = new SortedList<string, string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.Append("SELECT ");
        sb.Append($"{AppConsts.AttributeHash},"); // 0
        sb.Append($"{AppConsts.AttributeName},"); // 1
        sb.Append($"{AppConsts.AttributeVector},"); // 2
        sb.Append($"{AppConsts.AttributeRotateMode},"); // 3
        sb.Append($"{AppConsts.AttributeFlipMode},"); // 4
        sb.Append($"{AppConsts.AttributeLastView},"); // 5
        sb.Append($"{AppConsts.AttributeFamily},"); // 6
        sb.Append($"{AppConsts.AttributeScore},"); // 7
        sb.Append($"{AppConsts.AttributeLastCheck},"); // 8
        sb.Append($"{AppConsts.AttributeId}"); // 9
        sb.Append($" FROM {AppConsts.TableImages};");
        using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
        using var reader = sqlCommand.ExecuteReader();
        if (reader.HasRows) {
            var dtn = DateTime.Now;
            while (reader.Read()) {
                var hash = reader.GetString(0);
                var name = reader.GetString(1);
                var vector = Helper.ArrayToFloat((byte[])reader[2]);
                var rotatemode = (RotateMode)Enum.Parse(typeof(RotateMode), reader.GetInt64(3).ToString());
                var flipmode = (FlipMode)Enum.Parse(typeof(FlipMode), reader.GetInt64(4).ToString());
                var lastview = DateTime.FromBinary(reader.GetInt64(5));
                var family = (int)reader.GetInt64(6);
                var score = (int)reader.GetInt64(7);
                var lastcheck = DateTime.FromBinary(reader.GetInt64(8));
                var id = (int)reader.GetInt64(9);

                var img = new Img(
                    hash: hash,
                    name: name,
                    vector: vector,
                    rotatemode: rotatemode,
                    flipmode: flipmode,
                    lastview: lastview,
                    score: score,
                    lastcheck: lastcheck,
                    id: id,
                    family: family
                );

                imgList.Add(img.Hash, img);
                nameList.Add(img.Name, img.Hash);
                
                if (!(DateTime.Now.Subtract(dtn).TotalMilliseconds > AppConsts.TimeLapse)) {
                    continue;
                }

                dtn = DateTime.Now;
                var count = imgList.Count;
                progress?.Report($"Loading images ({count}){AppConsts.CharEllipsis}");
            }
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

    private static void LoadClusters(
        IProgress<string>? progress,
        out SortedList<int, DateTime> clusterList)
    {
        clusterList = new SortedList<int, DateTime>();
        var sb = new StringBuilder();
        sb.Append("SELECT ");
        sb.Append($"{AppConsts.AttributeId},"); // 0
        sb.Append($"{AppConsts.AttributeLastView}"); // 1
        sb.Append($" FROM {AppConsts.TableClusters};");
        using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
        using var reader = sqlCommand.ExecuteReader();
        if (reader.HasRows) {
            var dtn = DateTime.Now;
            while (reader.Read()) {
                var id = (int)reader.GetInt64(0);
                var lastview = DateTime.FromBinary(reader.GetInt64(1));

                clusterList.Add(id, lastview);

                if (!(DateTime.Now.Subtract(dtn).TotalMilliseconds > AppConsts.TimeLapse)) {
                    continue;
                }

                dtn = DateTime.Now;
                var count = clusterList.Count;
                progress?.Report($"Loading clusters ({count}){AppConsts.CharEllipsis}");
            }
        }
    }

    public static void Add(Img img)
    {
        lock (_lock) {
            using (var sqlCommand = _sqlConnection.CreateCommand()) {
                sqlCommand.Connection = _sqlConnection;
                var sb = new StringBuilder();
                sb.Append($"INSERT INTO {AppConsts.TableImages} (");
                sb.Append($"{AppConsts.AttributeHash},");
                sb.Append($"{AppConsts.AttributeName},");
                sb.Append($"{AppConsts.AttributeVector},");
                sb.Append($"{AppConsts.AttributeRotateMode},");
                sb.Append($"{AppConsts.AttributeFlipMode},");
                sb.Append($"{AppConsts.AttributeLastView},");
                sb.Append($"{AppConsts.AttributeFamily},");
                sb.Append($"{AppConsts.AttributeScore},");
                sb.Append($"{AppConsts.AttributeLastCheck},");
                sb.Append($"{AppConsts.AttributeId}");
                sb.Append(") VALUES (");
                sb.Append($"@{AppConsts.AttributeHash},");
                sb.Append($"@{AppConsts.AttributeName},");
                sb.Append($"@{AppConsts.AttributeVector},");
                sb.Append($"@{AppConsts.AttributeRotateMode},");
                sb.Append($"@{AppConsts.AttributeFlipMode},");
                sb.Append($"@{AppConsts.AttributeLastView},");
                sb.Append($"@{AppConsts.AttributeFamily},");
                sb.Append($"@{AppConsts.AttributeScore},");
                sb.Append($"@{AppConsts.AttributeLastCheck},");
                sb.Append($"@{AppConsts.AttributeId}");
                sb.Append(')');
                sqlCommand.CommandText = sb.ToString();
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", img.Hash);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeName}", img.Name);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVector}", img.GetRawVector());
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeRotateMode}", (int)img.RotateMode);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeFlipMode}", (int)img.FlipMode);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastView}", img.GetRawLastView());
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeFamily}", img.Family);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeScore}", img.Score);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastCheck}", img.GetRawLastCheck());
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeId}", img.Id);
                sqlCommand.ExecuteNonQuery();
            }
        }
    }

    public static void Delete(string hash)
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

    public static void DeleteAllClusters()
    {
        lock (_lock) {
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.Connection = _sqlConnection;
            sqlCommand.CommandText = $"DELETE FROM {AppConsts.TableClusters}";
            sqlCommand.ExecuteNonQuery();
            sqlCommand.CommandText = $"UPDATE {AppConsts.TableImages} SET {AppConsts.AttributeId} = 0";
            sqlCommand.ExecuteNonQuery();
        }
    }

    public static void AddCluster(int id, DateTime lastview)
    {
        lock (_lock) {
            using (var sqlCommand = _sqlConnection.CreateCommand()) {
                sqlCommand.Connection = _sqlConnection;
                var sb = new StringBuilder();
                sb.Append($"INSERT INTO {AppConsts.TableClusters} (");
                sb.Append($"{AppConsts.AttributeId},");
                sb.Append($"{AppConsts.AttributeLastView}");
                sb.Append(") VALUES (");
                sb.Append($"@{AppConsts.AttributeId},");
                sb.Append($"@{AppConsts.AttributeLastView}");
                sb.Append(')');
                sqlCommand.CommandText = sb.ToString();
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeId}", id);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastView}", lastview.Ticks);
                sqlCommand.ExecuteNonQuery();
            }
        }
    }

    public static void ClusterUpdateProperty(int id, string key, object val)
    {
        lock (_lock) {
            using (var sqlCommand = _sqlConnection.CreateCommand()) {
                sqlCommand.Connection = _sqlConnection;
                sqlCommand.CommandText =
                    $"UPDATE {AppConsts.TableClusters} SET {key} = @{key} WHERE {AppConsts.AttributeId} = @{AppConsts.AttributeId}";
                sqlCommand.Parameters.AddWithValue($"@{key}", val);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeId}", id);
                sqlCommand.ExecuteNonQuery();
            }
        }
    }
}