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

    private readonly float[] _center = new float[AppConsts.VectorSize];
    public ReadOnlySpan<float> Center {
        get { return _center; }
        set {
            if (value.Length != AppConsts.VectorSize) {
                throw new ArgumentException(
                    $"expected {AppConsts.VectorSize} floats, got {value.Length}", nameof(value));
            }

            value.CopyTo(_center);
            lock (_lock) {
                using var sqlCommand = _sqlConnection.CreateCommand();
                sqlCommand.Connection = _sqlConnection;
                sqlCommand.CommandText =
                    $"UPDATE {AppConsts.TableVars} SET {AppConsts.AttributeVector} = @{AppConsts.AttributeVector}";
                sqlCommand.Parameters.AddWithValue(
                    $"@{AppConsts.AttributeVector}", MemoryMarshal.Cast<float, byte>(_center).ToArray());
                sqlCommand.ExecuteNonQuery();
            }
        }
    }
}
