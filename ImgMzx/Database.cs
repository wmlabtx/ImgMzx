
using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;
using System.Text;

namespace ImgMzx;
public class Database : IDisposable
{
    private readonly Lock _lock =  new();
    private bool disposedValue;

    private readonly SqliteConnection _sqlConnection = new();

    public Database(string filedatabase)
    {
        _sqlConnection.ConnectionString = new SqliteConnectionStringBuilder {
            DataSource = filedatabase,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        _sqlConnection.Open();
    }

    public (ConcurrentDictionary<string, Img>, int) Load(IProgress<string>? progress)
    {
        var imgs = new ConcurrentDictionary<string, Img>();
        var maxImages = 0;

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
            sb.Append($"{AppConsts.AttributeVector} "); // 9
            sb.Append($"FROM {AppConsts.TableImages};");

            using (var command = new SqliteCommand(sb.ToString(), _sqlConnection))
            using (var reader = command.ExecuteReader()) {
                var dt = DateTime.Now;
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
                        Vector = reader.IsDBNull(9) ? [] : Helper.ArrayToFloat(reader.GetFieldValue<byte[]>(9))
                    };

                    imgs.TryAdd(img.Hash, img);

                    if (DateTime.Now.Subtract(dt).TotalMilliseconds >= AppConsts.TimeLapse) {
                        dt = DateTime.Now;
                        progress?.Report($"Loaded {imgs.Count} vectors...");
                    }
                }
            }

            sb.Clear();
            sb.Append($"SELECT {AppConsts.AttributeMaxImages} FROM {AppConsts.TableVars};");
            using (var command = new SqliteCommand(sb.ToString(), _sqlConnection))
            using (var reader = command.ExecuteReader()) {
                if (reader.Read()) {
                    maxImages = (int)reader.GetInt64(0);
                }
            }

            return (imgs, maxImages);
        }

    }

    public void AddImgToDatabase(Img img)
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
                sb.Append($"{AppConsts.AttributeVector}");
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
                sb.Append($"@{AppConsts.AttributeVector}");
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
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVector}", Helper.ArrayFromFloat(img.Vector));
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHistory}", img.History);
                sqlCommand.ExecuteNonQuery();
            }
        }
    }

    public void UpdateImgInDatabase(Img img)
    {
        lock (_lock) {
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.Connection = _sqlConnection;
            var sb = new StringBuilder();
            sb.Append($"UPDATE {AppConsts.TableImages} SET ");
            sb.Append($"{AppConsts.AttributeRotateMode} = @{AppConsts.AttributeRotateMode},");
            sb.Append($"{AppConsts.AttributeFlipMode} = @{AppConsts.AttributeFlipMode},");
            sb.Append($"{AppConsts.AttributeLastView} = @{AppConsts.AttributeLastView},");
            sb.Append($"{AppConsts.AttributeNext} = @{AppConsts.AttributeNext},");
            sb.Append($"{AppConsts.AttributeScore} = @{AppConsts.AttributeScore},");
            sb.Append($"{AppConsts.AttributeLastCheck} = @{AppConsts.AttributeLastCheck},");
            sb.Append($"{AppConsts.AttributeDistance} = @{AppConsts.AttributeDistance},");
            sb.Append($"{AppConsts.AttributeVector} = @{AppConsts.AttributeVector},");
            sb.Append($"{AppConsts.AttributeHistory} = @{AppConsts.AttributeHistory}");
            sb.Append($" WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}");
            sqlCommand.CommandText = sb.ToString();
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", img.Hash);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeRotateMode}", (int)img.RotateMode);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeFlipMode}", (int)img.FlipMode);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastView}", img.LastView.Ticks);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeNext}", img.Next);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeScore}", img.Score);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastCheck}", img.LastCheck.Ticks);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeDistance}", img.Distance);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVector}", Helper.ArrayFromFloat(img.Vector));
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHistory}", img.History);
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

    public void UpdateMaxImagesInDatabase(int maxImages)
    {
        lock (_lock) {
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.Connection = _sqlConnection;
            sqlCommand.CommandText =
                $"UPDATE {AppConsts.TableVars} SET {AppConsts.AttributeMaxImages} = @{AppConsts.AttributeMaxImages}";
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeMaxImages}", maxImages);
            sqlCommand.ExecuteNonQuery();
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue) {
            if (disposing) {
                lock (_lock) {
                    _sqlConnection?.Dispose();
                }
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}