using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Processing;
using System.Data;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ImgMzx;

public partial class Images(string filedatabase, string filevit) : IDisposable
{
    private readonly Lock _lock = new();
    private bool disposedValue;
    private readonly SqliteConnection _sqlConnection = new();
    private readonly Vit _vit = new(filevit);
    private readonly Panel?[] _imgPanels = { null, null };

    public bool ShowXOR;
    public Vit Vit => _vit;

    public SqliteConnection GetSqliteConnection()
    {
        return _sqlConnection;
    }

    public void Load(IProgress<string>? progress) {
        _sqlConnection.ConnectionString = new SqliteConnectionStringBuilder {
            DataSource = filedatabase,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        _sqlConnection.Open();
        _maxImages = 0;
        lock (_lock) {
            using var command = new SqliteCommand(
                $@"SELECT {AppConsts.AttributeMaxImages} FROM {AppConsts.TableVars};",
                _sqlConnection);
            using var reader = command.ExecuteReader();
            if (reader.Read()) {
                _maxImages = (int)reader.GetInt64(0);
            }
        }

        var numVectors = _maxImages + 10000;
        _vectors = new float[numVectors * AppConsts.VectorSize];
        var allVectorsBytes = MemoryMarshal.AsBytes(_vectors.AsSpan());
        var bytesPerVector = AppConsts.VectorSize * sizeof(float);
        var counter = 0;
        lock (_lock) {
            using var command = new SqliteCommand(
                $@"SELECT {AppConsts.AttributeHash}, {AppConsts.AttributeVector} FROM {AppConsts.TableImages};",
                _sqlConnection);
            using var reader = command.ExecuteReader(CommandBehavior.SequentialAccess);
            var dt = DateTime.Now;
            while (reader.Read()) {
                var hash = reader.GetString(0);
                using var stream = reader.GetStream(1);
                stream.ReadExactly(allVectorsBytes.Slice(counter * bytesPerVector, bytesPerVector));
                _hashToIndex[hash] = counter;
                counter++;
                if (DateTime.Now.Subtract(dt).TotalMilliseconds >= AppConsts.TimeLapse) {
                    dt = DateTime.Now;
                    progress?.Report($"Loaded {counter} vectors{AppConsts.CharEllipsis}");
                }
            }
        }

        _freeSlots = new Stack<int>(Enumerable.Range(counter, numVectors - counter).Reverse());
        progress?.Report($"Loaded {counter} vectors");
    }

    public (string Hash, float Distance)[] GetBeam(ReadOnlySpan<float> query)
    {
        lock (_lock) {
            var localQuery = query.ToArray();
            var hashArray = _hashToIndex.Keys.ToArray();
            var results = new (string Hash, float Distance)[hashArray.Length];

            Parallel.For(0, hashArray.Length, i =>
            {
                var hash = hashArray[i];
                var slot = _hashToIndex[hash];
                var vector = _vectors.AsSpan(slot * AppConsts.VectorSize, AppConsts.VectorSize);
                var distance = Vit.ComputeDistance(localQuery, vector);
                results[i] = (hash, distance);
            });

            Array.Sort(results, (a, b) => a.Distance.CompareTo(b.Distance));
            return results;
        }
    }

    public (string next, string message) GetNext(string hash, string? hashD = null)
    {
        var sb = new StringBuilder();

        var img = GetImgFromDatabase(hash);
        if (string.IsNullOrEmpty(img.Hash)) {
            return ("image not found", string.Empty);
        }

        if (img.Vector.Length != AppConsts.VectorSize) {
            var imagedata = AppFile.ReadMex(hash);
            if (imagedata != null) {
                using var image = AppBitmap.GetImage(imagedata);
                 if (image != null) {
                    var vector = _vit.CalculateVector(image);
                    if (vector != null) {
                        img.Vector = vector;
                    }
                }
            }
        }
        
        if (!string.IsNullOrEmpty(hashD)) {
            DeleteImgInDatabase(hashD);
        }

        var history = img.FromHistory();
        lock (_lock) {
            
            /*
            var changed = false;
            foreach (var h in history) {
                if (ContainsImg(h)) {
                    history.Remove(h);
                    changed = true;
                }
            }

            if (changed) {
                img.ToHistory(history);
            }
            */

            var beam = GetBeam(img.Vector);
            var next = string.Empty;
            var distance = 0f;
            for (var i = 0; i < beam.Length; i++) {
                if (beam[i].Hash.Equals(hash)) {
                    continue;
                }

                if (history.Contains(beam[i].Hash)) {
                    continue;
                }

                var imgY = GetImgFromDatabase(beam[i].Hash);
                if (string.IsNullOrEmpty(imgY.Hash)) {
                    continue;
                }

                distance = Vit.ComputeDistance(img.Vector, imgY.Vector);
                if (img.History.Length != imgY.History.Length) {
                    continue;
                }

                next = beam[i].Hash;
                break;
            }

            if (string.IsNullOrEmpty(next)) {
                 return ("no suitable next image found", string.Empty);
            }

            sb.Append($"{distance:F4} ");
            return (next, sb.ToString());
        }
    }

    public void Find(string? hashX, IProgress<string>? progress)
    {
        do {
            if (string.IsNullOrEmpty(hashX)) {
                hashX = GetHashLastView();
                if (string.IsNullOrEmpty(hashX)) {
                    var totalcount = GetCount();
                    progress?.Report($"totalcount = {totalcount}");
                    return;
                }
            }

            if (!SetLeftPanel(hashX)) {
                DeleteImgInDatabase(hashX);
                hashX = null;
                continue;
            }

            var imgX = GetImgFromDatabase(hashX);
            if (imgX.Vector.Length != AppConsts.VectorSize) {
                progress?.Report($"calculating vector{AppConsts.CharEllipsis}");
                var imagedata = AppFile.ReadMex(hashX);
                if (imagedata != null) {
                    using var image = AppBitmap.GetImage(imagedata);
                    if (image != null) {
                        var vector = _vit.CalculateVector(image);
                        if (vector != null) {
                            imgX.Vector = vector;
                        }
                    }
                }
                else {
                    hashX = null;
                    continue;
                }
            }

            var result = GetNext(hashX);
            var hashY = result.next;
            var message = result.message;
            if (!SetRightPanel(hashY)) {
                hashX = null;
                continue;
            }

            var sb = new StringBuilder();
            var totalimages = GetCount();
            var diff = totalimages - _maxImages;
            var historyCount = GetMinHistoryCount();
            sb.Append($"{historyCount}/{totalimages} ({diff}) ");
            var lastview = Helper.TimeIntervalToString(DateTime.Now.Subtract(imgX.LastView));
            sb.Append($"[{lastview} ago] ");
            sb.Append($"{message} ");
            progress?.Report(sb.ToString());
            break;
        }
        while (true);
    }

    public bool UpdateRightPanel()
    {
        return SetRightPanel(_imgPanels[1]!.Value.Hash);
    }

    public void Confirm(IProgress<string>? progress)
    {
        var hashX = _imgPanels[0]!.Value.Hash;
        var imgX = GetImgFromDatabase(hashX);
        var hashY = _imgPanels[1]!.Value.Hash;
        var imgY = GetImgFromDatabase(hashY);

        progress?.Report($"Calculating{AppConsts.CharEllipsis}");

        imgX.LastView = DateTime.Now;
        var hsX = imgX.FromHistory();
        if (hsX.Add(hashY)) {
            imgX.ToHistory(hsX);
        }

        imgY.LastView = DateTime.Now;
        var hsY = imgY.FromHistory();
        if (hsY.Add(hashX)) {
            imgY.ToHistory(hsY);
        }
    }

    public string? DeleteLeft(IProgress<string>? progress)
    {
        progress?.Report($"Calculating{AppConsts.CharEllipsis}");
        var hashX = _imgPanels[0]!.Value.Hash;
        var hashY = _imgPanels[1]!.Value.Hash;
        var vectorX = GetVector(hashX).ToArray();

        AppFile.DeleteMex(hashX, DateTime.Now);
        DeleteImgInDatabase(hashX);

        var imgY = GetImgFromDatabase(hashY);
        imgY.LastView = DateTime.Now;

        return FindClosest(vectorX);
    }

    public string? DeleteRight(IProgress<string>? progress)
    {
        progress?.Report($"Calculating{AppConsts.CharEllipsis}");
        var hashX = _imgPanels[0]!.Value.Hash;
        var hashY = _imgPanels[1]!.Value.Hash;
        var vectorY = GetVector(hashY).ToArray();

        AppFile.DeleteMex(hashY, DateTime.Now);
        DeleteImgInDatabase(hashY);

        var imgX = GetImgFromDatabase(hashX);
        imgX.LastView = DateTime.Now;

        return vectorY.Length == AppConsts.VectorSize ? FindClosest(vectorY) : null;
    }

    public static string Export(string hashE)
    {
        var imagedata = AppFile.ReadMex(hashE);
        if (imagedata != null) {
            var ext = AppBitmap.GetExtension(imagedata);
            var recycledName = AppFile.GetRecycledName(hashE, ext, AppConsts.PathExport, DateTime.Now);
            AppFile.CreateDirectory(recycledName);
            File.WriteAllBytes(recycledName, imagedata);
            var name = Path.GetFileName(recycledName);
            return name;
        }

        return string.Empty;
    }

    public void Export(IProgress<string>? progress)
    {
        progress?.Report($"Exporting{AppConsts.CharEllipsis}");
        var filename0 = Export(_imgPanels[0]!.Value.Hash);
        var filename1 = Export(_imgPanels[1]!.Value.Hash);
        progress?.Report($"Exported to {filename0} and {filename1}");
    }

    public void Rotate(string hash, RotateMode rotatemode, FlipMode flipmode)
    {
        var imagedata = AppFile.ReadMex(hash);
        if (imagedata == null) {
            return;
        }

        using var image = AppBitmap.GetImage(imagedata, rotatemode, flipmode);
        if (image == null) {
            return;
        }

        var rvector = _vit.CalculateVector(image);
        var img = GetImgFromDatabase(hash);
        img.Vector = rvector;
        img.RotateMode = rotatemode;
        img.FlipMode = flipmode;
    }

    public IEnumerable<string> GetAllHashes()
    {
        lock (_lock) {
            return [.. _hashToIndex.Keys];
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue) {
            if (disposing) {
                lock (_lock) {
                    _imgPanels[0]?.Image?.Dispose();
                    _imgPanels[1]?.Image?.Dispose();
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
