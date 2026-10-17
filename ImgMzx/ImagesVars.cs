using System.Runtime.InteropServices;

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

    private float[] _w = new float[AppConsts.VectorSize];
    public ReadOnlySpan<float> W {
        get { return _w; }
        set {
            value.CopyTo(_w);
            lock (_lock) {
                using var sqlCommand = _sqlConnection.CreateCommand();
                sqlCommand.Connection = _sqlConnection;
                sqlCommand.CommandText =
                    $"UPDATE {AppConsts.TableVars} SET {AppConsts.AttributeW} = @{AppConsts.AttributeW}";
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeW}", MemoryMarshal.Cast<float, byte>(value).ToArray());
                sqlCommand.ExecuteNonQuery();
            }
        }
    }

    private float _b;
    public float B {
        get { return _b; }
        set {
            _b = value;
            lock (_lock) {
                using var sqlCommand = _sqlConnection.CreateCommand();
                sqlCommand.Connection = _sqlConnection;
                sqlCommand.CommandText =
                    $"UPDATE {AppConsts.TableVars} SET {AppConsts.AttributeB} = @{AppConsts.AttributeB}";
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeB}", _b);
                sqlCommand.ExecuteNonQuery();
            }
        }
    }

    private float[] _mu = new float[AppConsts.VectorSize];
    public ReadOnlySpan<float> Mu {
        get { return _mu; }
        set {
            value.CopyTo(_mu);
            lock (_lock) {
                using var sqlCommand = _sqlConnection.CreateCommand();
                sqlCommand.Connection = _sqlConnection;
                sqlCommand.CommandText =
                    $"UPDATE {AppConsts.TableVars} SET {AppConsts.AttributeMu} = @{AppConsts.AttributeMu}";
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeMu}", MemoryMarshal.Cast<float, byte>(value).ToArray());
                sqlCommand.ExecuteNonQuery();
            }
        }
    }

    private float[] _sigma = new float[AppConsts.VectorSize];
    public ReadOnlySpan<float> Sigma {
        get { return _sigma; }
        set {
            value.CopyTo(_sigma);
            lock (_lock) {
                using var sqlCommand = _sqlConnection.CreateCommand();
                sqlCommand.Connection = _sqlConnection;
                sqlCommand.CommandText =
                    $"UPDATE {AppConsts.TableVars} SET {AppConsts.AttributeSigma} = @{AppConsts.AttributeSigma}";
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeSigma}", MemoryMarshal.Cast<float, byte>(value).ToArray());
                sqlCommand.ExecuteNonQuery();
            }
        }
    }
}
