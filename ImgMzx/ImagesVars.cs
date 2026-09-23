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
}
