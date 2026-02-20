using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Processing;
using System.Data;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ImgMzx;

public partial class Images(string filedatabase, string filevit, string filemask) : IDisposable
{
    private readonly Lock _lock = new();
    private bool disposedValue;
    private readonly SqliteConnection _sqlConnection = new();
    private readonly Vit _vit = new(filevit, filemask);
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

    public string GetNext(string hash, string? hashD = null)
    {
        var sb = new StringBuilder();
        var totalimages = GetCount();
        var nearGroup = GetNearGroup();
        var diff = totalimages - _maxImages;
        sb.Append($"{nearGroup}/{totalimages} ({diff}) ");

        var img = GetImgFromDatabase(hash);
        if (string.IsNullOrEmpty(img.Hash)) {
            return "image not found";
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
        
        var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(img.LastCheck));
        sb.Append($"[{lastcheck} ago] ");

        var hs = Helper.HistoryFromString(img.History);
        var hsnew = new HashSet<string>(StringComparer.Ordinal);
        foreach (var h in hs) {
            if (ContainsImg(h)) {
                hsnew.Add(h);
            }
        }

        if (hs.Count != hsnew.Count) {
            img.History = Helper.HistoryToString(hsnew);
        }

        var oldNext = img.Next;
        if (string.IsNullOrEmpty(oldNext)) {
            oldNext = "XXXX";
        }

        if (!string.IsNullOrEmpty(hashD)) {
            DeleteImgInDatabase(hashD);
        }

        lock (_lock) {
            var next = oldNext;
            var distance = 1f;
            var beam = GetBeam(img.Vector);
            for (var i = 0; i < beam.Length; i++) {
                if (beam[i].Hash.Equals(hash)) {
                    continue;
                }

                if (hsnew.Contains(beam[i].Hash)) {
                    continue;
                }

                next = beam[i].Hash;
                distance = beam[i].Distance;
                i++;
                break;
            }

            if (string.IsNullOrEmpty(next)) {
                return "no suitable next image found";
            }

            if (
                !oldNext.Equals(next) ||
                Math.Abs(img.Distance - distance) >= 0.0001f) {
                
                if (!oldNext.Equals(next)) {
                    img.Next = next;
                }

                if (Math.Abs(img.Distance - distance) >= 0.0001f) {
                    sb.Append($"{img.Distance:F4} {AppConsts.CharRightArrow} {distance:F4} ");
                    img.Distance = distance;
                }
            }

            img.LastCheck = DateTime.Now;
            return sb.ToString();
        }
    }

    public void Find(string? hashX, IProgress<string>? progress)
    {
        for (var i = 0; i < 100; i++) {
            var hashToCheck = GetHashLastCheck();
            if (hashToCheck == null) {
                 progress?.Report("nothing to show");
                return;
            }

            var imgToCheck = GetImgFromDatabase(hashToCheck);
            var message = GetNext(hashToCheck);
            if (string.IsNullOrEmpty(message)) {
                continue;
            }

            if (string.IsNullOrEmpty(message)) {
                DeleteImgInDatabase(hashToCheck);
                continue;
            }

            if (message.Contains(AppConsts.CharRightArrow)) {
                progress?.Report(message);
            }
        }

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

            var hashY = imgX.Next;
            if (!SetRightPanel(hashY)) {
                DeleteImgInDatabase(hashY);
                hashY = null;
                var message = GetNext(hashX);
                progress?.Report(message);
                imgX = GetImgFromDatabase(hashX);
                hashY = imgX.Next;
                if (!SetRightPanel(hashY)) {
                    hashX = null;
                    continue;
                }
            }

            var imgY = GetImgFromDatabase(hashY);
            var sb = new StringBuilder();
            var totalimages = GetCount();
            var nearGroup = GetNearGroup();
            var diff = totalimages - _maxImages;
            sb.Append($"{nearGroup}/{totalimages} ({diff}) ");
            var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(imgX.LastCheck));
            sb.Append($"[{lastcheck} ago] ");
            sb.Append($"{imgX.Distance:F4} ");
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

        imgX.Score = imgX.Score + 1;
        imgX.LastView = DateTime.Now;
        var hs = Helper.HistoryFromString(imgX.History);
        hs.Add(hashY);
        imgX.History = Helper.HistoryToString(hs);
        var message = GetNext(hashX);
        progress?.Report(message);

        imgY.Score = imgY.Score + 1;
        imgY.LastView = DateTime.Now;
        hs = Helper.HistoryFromString(imgY.History);
        hs.Add(hashX);
        imgY.History = Helper.HistoryToString(hs);
        message = GetNext(hashY);
        progress?.Report(message);
    }

    public void DeleteLeft(IProgress<string>? progress)
    {
        progress?.Report($"Calculating{AppConsts.CharEllipsis}");
        var hashX = _imgPanels[0]!.Value.Hash;
        var hashY = _imgPanels[1]!.Value.Hash;

        DeleteImgInDatabase(hashX);

        var imgY = GetImgFromDatabase(hashY);
        imgY.Score = imgY.Score + 1;
        imgY.LastView = DateTime.Now;
        var message = GetNext(hashY, hashX);
        progress?.Report(message);
    }

    public void DeleteRight(IProgress<string>? progress)
    {
        progress?.Report($"Calculating{AppConsts.CharEllipsis}");
        var hashX = _imgPanels[0]!.Value.Hash;
        var hashY = _imgPanels[1]!.Value.Hash;

        DeleteImgInDatabase(hashY);


        var imgX = GetImgFromDatabase(hashX);
        imgX.Score = imgX.Score + 1;
        imgX.LastView = DateTime.Now;
        var message = GetNext(hashX, hashY);
        progress?.Report(message);
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
                    if (_imgPanels[0]?.Image != null) {
                        _imgPanels[0]?.Image.Dispose();
                    }

                    if (_imgPanels[1]?.Image != null) {
                        _imgPanels[1]?.Image.Dispose();
                    }
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