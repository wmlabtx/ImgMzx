namespace ImgMzx;

public partial class Images : IDisposable
{
    private int _maxImages;
    public int MaxImages {
        get { return _maxImages; }
        set {
            _maxImages = value;
            lock (_lock) {
                using var sqlCommand = _sqlConnection.CreateCommand();
                sqlCommand.Connection = _sqlConnection;
                sqlCommand.CommandText =
                    $"UPDATE {AppConsts.TableVars} SET {AppConsts.AttributeMaxImages} = @{AppConsts.AttributeMaxImages}";
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeMaxImages}", _maxImages);
                sqlCommand.ExecuteNonQuery();
            }
        }
    }

    private int _recentIndex;
    public int RecentIndex {
        get { return _recentIndex; }
        set {
            _recentIndex = value;
            lock (_lock) {
                using var sqlCommand = _sqlConnection.CreateCommand();
                sqlCommand.Connection = _sqlConnection;
                sqlCommand.CommandText =
                    $"UPDATE {AppConsts.TableVars} SET [{AppConsts.AttributeIndex}] = @{AppConsts.AttributeIndex}";
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeIndex}", _recentIndex);
                sqlCommand.ExecuteNonQuery();
            }
        }
    }
}
