using Microsoft.Data.Sqlite;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ImgMzx;

public partial class Images : IDisposable
{
    private const int VectorSize = 768;
    private const int InitialCapacity = 200_000;
    private const int ImageSize = 224;
    private const int ChannelSize = ImageSize * ImageSize;
    private const int InputDataSize = 3 * ChannelSize;

    private readonly InferenceSession _session;
    private readonly SessionOptions? _sessionOptions;
    
    private readonly ConcurrentQueue<float[]> _inputDataPool = new();
    private readonly ConcurrentQueue<float[]> _vectorPool = new();
    private readonly ConcurrentQueue<List<NamedOnnxValue>> _containerPool = new();

    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, int> _hashToIndex;
    private readonly SqliteConnection _sqlConnection = new();
    private readonly Panel?[] _imgPanels = { null, null };

    private bool disposedValue;

    private int _maxImages;
    private Memory<float> _vectors;
    private IMemoryOwner<float> _vectorsOwner;
    private string[] _hashes;
    private int _countVectors;
    private int _capacityVectors;

    public bool ShowXOR;

    public int MaxImages => _maxImages;

    public Images(string filedatabase, string filevit)
    {
        _sessionOptions = new SessionOptions {
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
            EnableCpuMemArena = true,
            EnableMemoryPattern = true,
            ExecutionMode = ExecutionMode.ORT_PARALLEL,
            InterOpNumThreads = Environment.ProcessorCount,
            IntraOpNumThreads = Environment.ProcessorCount,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        _sessionOptions.AppendExecutionProvider_CPU();
        _session = new InferenceSession(filevit, _sessionOptions);

        _capacityVectors = InitialCapacity;
        _hashToIndex = new ConcurrentDictionary<string, int>(Environment.ProcessorCount, _capacityVectors, StringComparer.Ordinal);
        _vectorsOwner = MemoryPool<float>.Shared.Rent(_capacityVectors * VectorSize);
        _vectors = _vectorsOwner.Memory[..(_capacityVectors * VectorSize)];
        _hashes = new string[_capacityVectors];
        _countVectors = 0;

        var connectionString = $"Data Source={filedatabase};";
        _sqlConnection = new SqliteConnection(connectionString);
        _sqlConnection.Open();

        using (var pragmaCommand = _sqlConnection.CreateCommand()) {
            pragmaCommand.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA temp_store = MEMORY;";
            pragmaCommand.ExecuteNonQuery();
        }

        _maxImages = InitialCapacity;
        var sb = new StringBuilder();
        sb.Append("SELECT ");
        sb.Append($"{AppConsts.AttributeMaxImages}");
        sb.Append($" FROM {AppConsts.TableVars};");
        using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
        using var reader = sqlCommand.ExecuteReader();
        if (reader.HasRows) {
            while (reader.Read()) {
                _maxImages = reader.GetInt32(0);
                break;
            }
        }
    }

    public Panel? GetPanel(int id)
    {
        return (id == 0 || id == 1) ? _imgPanels[id] : null;
    }

    public float[] CalculateVector(Image<Rgb24> image)
    {
        var inputData = _inputDataPool.TryDequeue(out var pooledInputData) && pooledInputData.Length == InputDataSize
            ? pooledInputData 
            : new float[InputDataSize];

        var vectorData = _vectorPool.TryDequeue(out var pooledVector) && pooledVector.Length == VectorSize
            ? pooledVector 
            : new float[VectorSize];

        var container = _containerPool.TryDequeue(out var pooledContainer) 
            ? pooledContainer 
            : new List<NamedOnnxValue>(1);
        
        container.Clear();

        try {
            ProcessImageOptimized(image, inputData);

            var tensor = new DenseTensor<float>(inputData.AsMemory(0, InputDataSize), new int[] { 1, 3, ImageSize, ImageSize });
            container.Add(NamedOnnxValue.CreateFromTensor("pixel_values", tensor));

            using var results = _session.Run(container);
            var outputTensor = results[0].AsTensor<float>() ?? throw new InvalidOperationException("Model output is null");
            ExtractVectorOptimized(outputTensor, vectorData);
            NormalizeVectorSIMD(vectorData.AsSpan(0, VectorSize));

            var result = new float[VectorSize];
            Array.Copy(vectorData, result, VectorSize);
            return result;
        }
        finally
        {
            if (inputData.Length == InputDataSize && _inputDataPool.Count < 8) {
                Array.Clear(inputData);
                _inputDataPool.Enqueue(inputData);
            }

            if (vectorData.Length == VectorSize && _vectorPool.Count < 8) {
                Array.Clear(vectorData);
                _vectorPool.Enqueue(vectorData);
            }

            if (_containerPool.Count < 8) {
                container.Clear();
                _containerPool.Enqueue(container);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessImageOptimized(Image<Rgb24> image, float[] inputData)
    {
        using var resizedImage = image.Clone(ctx => ctx.Resize(ImageSize, ImageSize));
        var pixelData = new Rgb24[ImageSize * ImageSize];
        resizedImage.ProcessPixelRows(accessor => {
            for (int y = 0; y < ImageSize; y++) {
                var row = accessor.GetRowSpan(y);
                row.CopyTo(pixelData.AsSpan(y * ImageSize, ImageSize));
            }
        });

        const float scale = 1.0f / 255.0f;
        const float rMean = 0.485f, gMean = 0.456f, bMean = 0.406f;
        const float rStd = 0.229f, gStd = 0.224f, bStd = 0.225f;

        var rSpan = inputData.AsSpan(0, ChannelSize);
        var gSpan = inputData.AsSpan(ChannelSize, ChannelSize);
        var bSpan = inputData.AsSpan(2 * ChannelSize, ChannelSize);

        if (Vector.IsHardwareAccelerated && pixelData.Length >= Vector<float>.Count) {
            ProcessPixelsVectorized(pixelData, rSpan, gSpan, bSpan, scale, rMean, gMean, bMean, rStd, gStd, bStd);
        }
        else {
            ProcessPixelsScalar(pixelData, rSpan, gSpan, bSpan, scale, rMean, gMean, bMean, rStd, gStd, bStd);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessPixelsVectorized(ReadOnlySpan<Rgb24> pixels, Span<float> rSpan, Span<float> gSpan, Span<float> bSpan,
        float scale, float rMean, float gMean, float bMean, float rStd, float gStd, float bStd)
    {
        var scaleVec = new Vector<float>(scale);
        var rMeanVec = new Vector<float>(rMean);
        var gMeanVec = new Vector<float>(gMean);
        var bMeanVec = new Vector<float>(bMean);
        var rStdVec = new Vector<float>(1.0f / rStd);
        var gStdVec = new Vector<float>(1.0f / gStd);
        var bStdVec = new Vector<float>(1.0f / bStd);

        var vectorCount = Vector<float>.Count;
        var vectorizedLength = (pixels.Length / vectorCount) * vectorCount;

        var rValues = ArrayPool<float>.Shared.Rent(vectorCount);
        var gValues = ArrayPool<float>.Shared.Rent(vectorCount);
        var bValues = ArrayPool<float>.Shared.Rent(vectorCount);

        try {
            for (int i = 0; i < vectorizedLength; i += vectorCount) {
                for (int j = 0; j < vectorCount && (i + j) < pixels.Length; j++) {
                    var pixel = pixels[i + j];
                    rValues[j] = pixel.R;
                    gValues[j] = pixel.G;
                    bValues[j] = pixel.B;
                }

                var rVec = new Vector<float>(rValues);
                var gVec = new Vector<float>(gValues);
                var bVec = new Vector<float>(bValues);

                rVec = (rVec * scaleVec - rMeanVec) * rStdVec;
                gVec = (gVec * scaleVec - gMeanVec) * gStdVec;
                bVec = (bVec * scaleVec - bMeanVec) * bStdVec;

                rVec.CopyTo(rSpan.Slice(i, vectorCount));
                gVec.CopyTo(gSpan.Slice(i, vectorCount));
                bVec.CopyTo(bSpan.Slice(i, vectorCount));
            }
        }
        finally {
            ArrayPool<float>.Shared.Return(rValues);
            ArrayPool<float>.Shared.Return(gValues);
            ArrayPool<float>.Shared.Return(bValues);
        }

        for (int i = vectorizedLength; i < pixels.Length; i++) {
            var pixel = pixels[i];
            rSpan[i] = (pixel.R * scale - rMean) / rStd;
            gSpan[i] = (pixel.G * scale - gMean) / gStd;
            bSpan[i] = (pixel.B * scale - bMean) / bStd;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessPixelsScalar(ReadOnlySpan<Rgb24> pixels, Span<float> rSpan, Span<float> gSpan, Span<float> bSpan,
        float scale, float rMean, float gMean, float bMean, float rStd, float gStd, float bStd)
    {
        for (int i = 0; i < pixels.Length; i++) {
            var pixel = pixels[i];
            rSpan[i] = (pixel.R * scale - rMean) / rStd;
            gSpan[i] = (pixel.G * scale - gMean) / gStd;
            bSpan[i] = (pixel.B * scale - bMean) / bStd;
        }
    }

    private static void ExtractVectorOptimized(Tensor<float> outputTensor, float[] vectorArray)
    {
        var shape = outputTensor.Dimensions.ToArray();
        var vectorSpan = vectorArray.AsSpan(0, VectorSize);

        if (shape.Length == 3) {
            var hiddenSize = Math.Min(shape[2], VectorSize);
            for (var i = 0; i < hiddenSize; i++) {
                vectorSpan[i] = outputTensor[0, 0, i];
            }
        }
        else if (shape.Length == 2) {
            var hiddenSize = Math.Min(shape[1], VectorSize);
            for (var i = 0; i < hiddenSize; i++) {
                vectorSpan[i] = outputTensor[0, i];
            }
        }
        else {
            var copyLength = Math.Min(outputTensor.Length, VectorSize);
            for (int i = 0; i < copyLength; i++) {
                vectorSpan[i] = outputTensor.GetValue(i);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void NormalizeVectorSIMD(Span<float> vector)
    {
        var sumOfSquares = 0.0f;

        if (Vector.IsHardwareAccelerated && vector.Length >= Vector<float>.Count) {
            var vectorSpan = MemoryMarshal.Cast<float, Vector<float>>(vector);
            var sumVector = Vector<float>.Zero;

            foreach (var v in vectorSpan) {
                sumVector += v * v;
            }

            for (int i = 0; i < Vector<float>.Count; i++) {
                sumOfSquares += sumVector[i];
            }

            var remaining = vector.Length % Vector<float>.Count;
            var remainingStart = vector.Length - remaining;
            for (int i = remainingStart; i < vector.Length; i++) {
                sumOfSquares += vector[i] * vector[i];
            }
        }
        else {
            foreach (var value in vector) {
                sumOfSquares += value * value;
            }
        }

        var norm = (float)Math.Sqrt(sumOfSquares);
        if (norm > 0) {
            var invNorm = 1.0f / norm;

            if (Vector.IsHardwareAccelerated && vector.Length >= Vector<float>.Count) {
                var invNormVector = new Vector<float>(invNorm);
                var vectorCount = Vector<float>.Count;

                for (int i = 0; i <= vector.Length - vectorCount; i += vectorCount) {
                    var vec = new Vector<float>(vector.Slice(i, vectorCount));
                    (vec * invNormVector).CopyTo(vector.Slice(i, vectorCount));
                }

                var remaining = vector.Length % vectorCount;
                var remainingStart = vector.Length - remaining;
                for (int i = remainingStart; i < vector.Length; i++) {
                    vector[i] *= invNorm;
                }
            }
            else {
                for (int i = 0; i < vector.Length; i++) {
                    vector[i] *= invNorm;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CalculateDistance(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        if (x.IsEmpty || y.IsEmpty || x.Length != y.Length)
            return 1f;

        var dotProduct = ComputeDotProductSIMD(x, y);
        var distance = 1f - dotProduct;

        return Math.Max(0f, Math.Min(1f, distance));
    }

    public static float CalculateDistance(float[] x, float[] y)
    {
        if (x.Length == 0 || y.Length == 0 || x.Length != y.Length)
            return 1f;

        var dotProduct = ComputeDotProductSIMD(x.AsSpan(), y.AsSpan());
        var distance = 1f - dotProduct;

        return Math.Max(0f, Math.Min(1f, distance));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ComputeDotProductSIMD(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        var dotProduct = 0.0f;

        if (Vector.IsHardwareAccelerated && x.Length >= Vector<float>.Count) {
            var xVectors = MemoryMarshal.Cast<float, Vector<float>>(x);
            var yVectors = MemoryMarshal.Cast<float, Vector<float>>(y);
            var sumVector = Vector<float>.Zero;

            for (int i = 0; i < xVectors.Length; i++) {
                sumVector += xVectors[i] * yVectors[i];
            }

            for (int i = 0; i < Vector<float>.Count; i++) {
                dotProduct += sumVector[i];
            }

            var remaining = x.Length % Vector<float>.Count;
            var remainingStart = x.Length - remaining;
            for (int i = remainingStart; i < x.Length; i++) {
                dotProduct += x[i] * y[i];
            }
        }
        else {
            for (int i = 0; i < x.Length; i++) {
                dotProduct += x[i] * y[i];
            }
        }

        return dotProduct;
    }

    public void LoadVectorsFromDatabase(IProgress<string>? progress)
    {
        var sb = new StringBuilder();
        sb.Append("SELECT ");
        sb.Append($"{AppConsts.AttributeHash}, ");
        sb.Append($"{AppConsts.AttributeVector} ");
        sb.Append($"FROM {AppConsts.TableImages};");

        using var command = new SqliteCommand(sb.ToString(), _sqlConnection);
        using var reader = command.ExecuteReader();

        var tempVectors = ArrayPool<float>.Shared.Rent(VectorSize);
        var dt = DateTime.Now;
        try {
            while (reader.Read()) {
                var hash = reader.GetString(0);
                var vectorBytes = (byte[])reader[1];

                if (vectorBytes.Length == VectorSize * sizeof(float)) {
                    var vectorSpan = MemoryMarshal.Cast<byte, float>(vectorBytes.AsSpan());
                    if (vectorSpan.Length == VectorSize) {
                        vectorSpan.CopyTo(tempVectors.AsSpan(0, VectorSize));
                        AddVector(hash, tempVectors.AsSpan(0, VectorSize));
                    }
                }

                if (DateTime.Now.Subtract(dt).TotalMilliseconds >= AppConsts.TimeLapse) {
                    dt = DateTime.Now;
                    progress?.Report($"Loaded {_countVectors} vectors...");
                }
            }
        }
        finally {
            ArrayPool<float>.Shared.Return(tempVectors);
        }
    }

    public ReadOnlySpan<float> GetVectorSpan(string hash)
    {
        if (string.IsNullOrEmpty(hash) || !_hashToIndex.TryGetValue(hash, out var index)) {
            throw new ArgumentException($"Hash '{hash}' not found");
        }

        return GetVectorSpan(index);
    }

    private ReadOnlySpan<float> GetVectorSpan(int index)
    {
        var offset = index * VectorSize;
        return _vectors.Span.Slice(offset, VectorSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Span<float> GetMutableVectorSpan(int index)
    {
        var offset = index * VectorSize;
        return _vectors.Span.Slice(offset, VectorSize);
    }

    private void ExpandCapacity()
    {
        var newCapacity = _capacityVectors + 1000;

        var newVectorsOwner = MemoryPool<float>.Shared.Rent(newCapacity * VectorSize);
        var newVectors = newVectorsOwner.Memory[..(newCapacity * VectorSize)];
        var newHashes = new string[newCapacity];

        _vectors.Span[..(_countVectors * VectorSize)].CopyTo(newVectors.Span);
        Array.Copy(_hashes, newHashes, _countVectors);

        _vectorsOwner?.Dispose();
        _vectorsOwner = newVectorsOwner;
        _vectors = newVectors;
        _hashes = newHashes;
        _capacityVectors = newCapacity;
    }

    public bool AddVector(string hash, float[] vector)
    {
        return AddVector(hash, vector.AsSpan());
    }

    private bool AddVector(string hash, ReadOnlySpan<float> vector)
    {
        if (string.IsNullOrEmpty(hash)) { 
            throw new ArgumentNullException(nameof(hash)); 
        }

        if (vector.Length != VectorSize) { 
            throw new ArgumentException($"Vector must have exactly {VectorSize} elements", nameof(vector)); 
        }

        lock (_lock) {
            if (_hashToIndex.ContainsKey(hash)) {
                return false;
            }

            if (_countVectors >= _capacityVectors) {
                ExpandCapacity();
            }

            var index = _countVectors++;
            _hashes[index] = hash;

            var targetSpan = GetMutableVectorSpan(index);
            vector.CopyTo(targetSpan);

            _hashToIndex[hash] = index;
            return true;
        }
    }

    public void ChangeVector(string hash, float[] vector)
    {
        ChangeVector(hash, vector.AsSpan());
    }

    private void ChangeVector(string hash, ReadOnlySpan<float> vector)
    {
        if (string.IsNullOrEmpty(hash)) {
            throw new ArgumentNullException(nameof(hash));
        }

        if (vector.Length != VectorSize) {
            throw new ArgumentException($"Vector must have exactly {VectorSize} elements", nameof(vector));
        }

        if (!_hashToIndex.TryGetValue(hash, out var index)) {
            throw new ArgumentException($"Hash '{hash}' not found");
        }

        var targetSpan = GetMutableVectorSpan(index);
        vector.CopyTo(targetSpan);
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
            _maxImages = maxImages;
        }
    }

    public int GetCountFromDatabase()
    {
        lock (_lock) {
            var sb = new StringBuilder();
            sb.Append($"SELECT COUNT(*) FROM {AppConsts.TableImages};");

            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            using var reader = sqlCommand.ExecuteReader();

            if (reader.HasRows && reader.Read()) {
                return reader.GetInt32(0);
            }

            return 0;
        }
    }

    public Img? GetImgFromDatabase(string hash)
    {
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
            sb.Append($"{AppConsts.AttributeHistory}"); // 8
            sb.Append($" FROM {AppConsts.TableImages}");
            sb.Append($" WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}");
            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
            using var reader = sqlCommand.ExecuteReader();
            if (reader.HasRows) {
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
                        History = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
                    };

                    return img;
                }
            }

            return null;
        }
    }

    public bool ContainsImgInDatabase(string hash)
    {
        lock (_lock) {
            var sb = new StringBuilder();
            sb.Append($"SELECT 1 FROM {AppConsts.TableImages}");
            sb.Append($" WHERE {AppConsts.AttributeHash} = @hash LIMIT 1;");
            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            sqlCommand.Parameters.AddWithValue("@hash", hash);
            using var reader = sqlCommand.ExecuteReader();
            return reader.HasRows;
        }
    }

    public void AddImgToDatabase(Img img, float[] vector)
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
                sb.Append($"{AppConsts.AttributeHistory}");
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
                sb.Append($"@{AppConsts.AttributeHistory}");
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
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVector}", Helper.ArrayFromFloat(vector));
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHistory}", img.History);
                sqlCommand.ExecuteNonQuery();
            }
        }
    }

    public void UpdateImgInDatabase(string hash, string key, object val)
    {
        lock (_lock) {
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.CommandText =
                $"UPDATE {AppConsts.TableImages} SET {key} = @value WHERE {AppConsts.AttributeHash} = @hash";
            sqlCommand.Parameters.AddWithValue("@value", val ?? DBNull.Value);
            sqlCommand.Parameters.AddWithValue("@hash", hash);
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

    public string? GetLastCheckFromDatabase()
    {
        lock (_lock) {
            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append($"{AppConsts.AttributeHash} ");
            sb.Append($"FROM {AppConsts.TableImages} ");
            sb.Append($"ORDER BY {AppConsts.AttributeLastCheck} ");
            sb.Append("LIMIT 1;");

            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            using var reader = sqlCommand.ExecuteReader();

            if (reader.HasRows && reader.Read()) {
                return reader.GetString(0);
            }

            return null;
        }
    }

    public DateTime? GetLastViewFromDatabase()
    {
        lock (_lock) {
            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append($"MIN({AppConsts.AttributeLastView}) ");
            sb.Append($"FROM {AppConsts.TableImages} ");

            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            using var reader = sqlCommand.ExecuteReader();

            if (reader.HasRows && reader.Read()) {
                return new DateTime(reader.GetInt64(0));
            }

            return null;
        }
    }

    private int _previousHistLen = -1;

    public string? GetX()
    {
        lock (_lock) {
            var sb = new StringBuilder();
            
            sb.Append("SELECT ");
            sb.Append($"  LENGTH({AppConsts.AttributeHistory}) as hist_len, ");
            sb.Append($"  COUNT(*) as cnt ");
            sb.Append($"FROM {AppConsts.TableImages} ");
            sb.Append($"GROUP BY hist_len ");
            sb.Append($"ORDER BY hist_len;");

            var histGroups = new List<(int histLen, int count)>();
            using (var cmd = new SqliteCommand(sb.ToString(), _sqlConnection)) {
                using (var reader = cmd.ExecuteReader()) {
                    while (reader.Read()) {
                        histGroups.Add((reader.GetInt32(0), reader.GetInt32(1)));
                    }
                }
            }

            if (histGroups.Count == 0) {
                return null;
            }

            var selectedHistLen = -1;
            do {
                selectedHistLen = histGroups[^1].histLen;
                for (var i = 0; i < histGroups.Count; i++) {
                    if (i == histGroups.Count - 1 || Random.Shared.Next(10) > 0) {
                        selectedHistLen = histGroups[i].histLen;
                        break;
                    }
                }
            } while (selectedHistLen == -1);
            
            var imode = Random.Shared.Next(15);
            var smode = imode switch {
                0 => $"{AppConsts.AttributeDistance} LIMIT 1",
                1 => $"{AppConsts.AttributeDistance} DESC LIMIT 1",
                2 => $"{AppConsts.AttributeScore} LIMIT 1",
                3 => $"{AppConsts.AttributeLastCheck} DESC LIMIT 1",
                4 => $"{AppConsts.AttributeLastView} DESC LIMIT 1000 OFFSET 1000",
                _ => $"{AppConsts.AttributeLastView} LIMIT 1"

            };

            sb.Clear();
            sb.Append($"SELECT {AppConsts.AttributeHash} ");
            sb.Append($"FROM {AppConsts.TableImages} ");
            sb.Append($"ORDER BY {smode}");


            using (var cmd = new SqliteCommand(sb.ToString(), _sqlConnection)) {
                cmd.Parameters.AddWithValue("@histLen", selectedHistLen);
                using (var reader = cmd.ExecuteReader()) {
                    if (reader.HasRows && reader.Read()) {
                        return reader.GetString(0);
                    }
                }
            }

            return null;
        }
    }

    private static float ComputeDistance(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        var dotProduct = 0f;
        for (int i = 0; i < VectorSize; i++) {
            dotProduct += x[i] * y[i];
        }

        var distance = 1f - dotProduct;
        return Math.Max(0f, Math.Min(1f, distance));
    }

    public (string Hash, float Distance)[] GetBeam(ReadOnlySpan<float> query)
    {
        lock (_lock) {

            var results = new (string Hash, float Distance)[_countVectors];
            var localquery = query.ToArray();
            Parallel.For(0, _countVectors, i => {
                var hash = _hashes[i];
                var index = _hashToIndex[hash];
                var vector = GetVectorSpan(index);
                var distance = ComputeDistance(localquery, vector);
                results[i] = (hash, distance);
            });

            return [.. results.OrderBy(x => x.Distance)];
        }
    }

    public string GetNext(string hash, string? hashD = null)
    {
        string message;

        var img = GetImgFromDatabase(hash);
        if (img == null) {
            return "image not found";
        }

        var hs = Helper.HistoryFromString(img.Value.History);
        var hsnew = new HashSet<string>(StringComparer.Ordinal);
        foreach (var h in hs) {
            if (ContainsImgInDatabase(h)) {
                hsnew.Add(h);
            }
        }

        if (hs.Count != hsnew.Count) {
            var history = Helper.HistoryToString(hsnew);
            UpdateImgInDatabase(hash, AppConsts.AttributeHistory, history);
            img = GetImgFromDatabase(hash);
            if (img == null) {
                return "image not found";
            }

            hsnew.Clear();
            hs.Clear();
            hs = Helper.HistoryFromString(img.Value.History);
        }

        var oldNext = img.Value.Next;
        if (string.IsNullOrEmpty(oldNext)) {
            oldNext = "XXXX";
        }

        if (!string.IsNullOrEmpty(hashD)) {
            DeleteImgInDatabase(hashD);
            AppFile.DeleteMex(hashD, DateTime.Now);
        }

        lock (_lock) {
            if (!_hashToIndex.TryGetValue(hash, out var index)) {
                return "image not found";
            }

            var vector = GetVectorSpan(index);
            var next = oldNext;
            var distance = 1f;

            var beam = GetBeam(vector);
            for (var i = 0; i < beam.Length; i++) {
                if (beam[i].Hash.Equals(hash)) {
                    continue;
                }

                if (!ContainsImgInDatabase(beam[i].Hash)) {
                    continue;
                }

                if (hs.Contains(beam[i].Hash)) {
                    continue;
                }

                next = beam[i].Hash;
                distance = beam[i].Distance;
                break;
            }

            if (string.IsNullOrEmpty(next)) {
                return "no suitable next image found";
            }

            if (!oldNext.Equals(next) || Math.Abs(img.Value.Distance - distance) >= 0.0001f) {
                message = $"s{img.Value.Score} {oldNext[..4]}{AppConsts.CharEllipsis} {img.Value.Distance:F4} {AppConsts.CharRightArrow} {next[..4]}{AppConsts.CharEllipsis} {distance:F4}";
                UpdateImgInDatabase(hash, AppConsts.AttributeNext, next);
                UpdateImgInDatabase(hash, AppConsts.AttributeDistance, distance);
            }
            else {
                message = $"{distance:F4}";
            }

            UpdateImgInDatabase(hash, AppConsts.AttributeLastCheck, DateTime.Now.Ticks);
            return message;
        }
    }

    private bool SetPanel(
        string hash,
        out byte[]? imagedata,
        out Img? img,
        out Image<Rgb24>? image,
        out string extension,
        out DateTime? taken)
    {
        imagedata = null;
        img = null;
        image = null;
        extension = "xxx";
        taken = null;

        if (!AppHash.IsValidHash(hash) || !ContainsImgInDatabase(hash)) {
            return false;
        }

        imagedata = AppFile.ReadMex(hash);
        if (imagedata == null) {
            return false;
        }

        extension = AppBitmap.GetExtension(imagedata);
        img = GetImgFromDatabase(hash);
        if (img == null) {
            return false;
        }

        image = AppBitmap.GetImage(imagedata, img.Value.RotateMode, img.Value.FlipMode);
        if (image == null) {
            return false;
        }

        taken = AppBitmap.GetDateTaken(image);
        return true;
    }

    public bool SetLeftPanel(string hash)
    {
        if (_imgPanels[0]?.Image != null) {
            _imgPanels[0]?.Image.Dispose();
        }

        if (!SetPanel(hash,
                out var imagedata,
                out var img,
                out var image,
                out var extension,
                out var taken)) {
            return false;
        }

        Debug.Assert(imagedata != null);
        Debug.Assert(img != null);
        Debug.Assert(image != null);

        var imgpanel = new Panel {
            Hash = hash,
            Img = img.Value,
            Size = imagedata.LongLength,
            Image = image,
            Extension = extension,
            Taken = taken
        };

        _imgPanels[0] = imgpanel;

        return true;
    }

    public bool SetRightPanel(string hash)
    {
        if (_imgPanels[1]?.Image != null) {
            _imgPanels[1]?.Image.Dispose();
        }

        if (!SetPanel(hash,
                out var imagedata,
                out var img,
                out var image,
                out var extension,
                out var taken)) {
            return false;
        }

        Debug.Assert(imagedata != null);
        Debug.Assert(img != null);
        Debug.Assert(image != null);

        if (ShowXOR) {
            AppBitmap.Composite(_imgPanels[0]!.Value.Image, image, out var imagexor);
            image.Dispose();
            image = imagexor;
        }

        var imgpanel = new Panel {
            Hash = hash,
            Img = img.Value,
            Size = imagedata.LongLength,
            Image = image,
            Extension = extension,
            Taken = taken
        };

        _imgPanels[1] = imgpanel;

        return true;
    }

    public void Find(string? hashX, IProgress<string>? progress)
    {
        for (var i = 0; i< 10; i++) {
            var hashToCheck = GetLastCheckFromDatabase();
            if (hashToCheck == null) {
                 progress?.Report("nothing to show");
                return;
            }

            var imgToCheck = GetImgFromDatabase(hashToCheck);
            if (imgToCheck != null) {
                var message = GetNext(hashToCheck);
                var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(imgToCheck.Value.LastCheck));
                progress?.Report($"[{lastcheck} ago] {hashToCheck[..4]}{AppConsts.CharEllipsis}: {message}");
            }
        }

        var sb = new StringBuilder();
        do {
            sb.Clear();
            var totalimages = GetCountFromDatabase();
            var diff = totalimages - _maxImages;
            sb.Append($"{totalimages} ({diff}) ");

            if (string.IsNullOrEmpty(hashX)) {
                hashX = GetX();
                if (string.IsNullOrEmpty(hashX)) {
                    var totalcount = GetCountFromDatabase();
                    progress?.Report($"totalcount = {totalcount}");
                    return;
                }
            }

            if (!SetLeftPanel(hashX)) {
                DeleteImgInDatabase(hashX);
                AppFile.DeleteMex(hashX, DateTime.Now);
                hashX = null;
                continue;
            }

            var imgX = GetImgFromDatabase(hashX);
            if (imgX == null) {
                DeleteImgInDatabase(hashX);
                AppFile.DeleteMex(hashX, DateTime.Now);
                hashX = null;
                continue;
            }

            var lastcheck = Helper.TimeIntervalToString(DateTime.Now.Subtract(imgX.Value.LastCheck));
            sb.Append($"[{lastcheck} ago] {hashX[..4]}: ");

            var hashY = imgX.Value.Next;
            if (!SetRightPanel(hashY)) {
                var message = GetNext(hashX);
                sb.Append(message);
                imgX = GetImgFromDatabase(hashX);
                if (imgX == null) {
                    DeleteImgInDatabase(hashX);
                    AppFile.DeleteMex(hashX, DateTime.Now);
                    hashX = null;
                    continue;
                }

                hashY = imgX.Value.Next;
                if (!SetRightPanel(hashY)) {
                    hashX = null;
                    continue;
                }
            }
            else {
                sb.Append($"= {imgX.Value.Distance:F4}");
            }

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

        if (imgX != null && imgY != null) {
            UpdateImgInDatabase(hashX, AppConsts.AttributeScore, imgX.Value.Score + 1);
            UpdateImgInDatabase(hashY, AppConsts.AttributeScore, imgY.Value.Score + 1);
            UpdateImgInDatabase(hashX, AppConsts.AttributeLastView, DateTime.Now.Ticks);
            UpdateImgInDatabase(hashY, AppConsts.AttributeLastView, DateTime.Now.Ticks);

            var hs = Helper.HistoryFromString(imgX.Value.History);
            hs.Add(hashY);
            var history = Helper.HistoryToString(hs);
            UpdateImgInDatabase(hashX, AppConsts.AttributeHistory, history);

            hs = Helper.HistoryFromString(imgY.Value.History);
            hs.Add(hashX);
            history = Helper.HistoryToString(hs);
            UpdateImgInDatabase(hashY, AppConsts.AttributeHistory, history);

            progress?.Report($"Calculating{AppConsts.CharEllipsis}");
            var message = GetNext(hashX);
            progress?.Report(message);
            message = GetNext(hashY);
            progress?.Report(message);
        }
    }

    public void DeleteLeft(IProgress<string>? progress)
    {
        progress?.Report($"Calculating{AppConsts.CharEllipsis}");
        var hashX = _imgPanels[0]!.Value.Hash;
        var hashY = _imgPanels[1]!.Value.Hash;
        var message = GetNext(hashY, hashX);
        var imgY = GetImgFromDatabase(hashY);
        if (imgY != null) {
            UpdateImgInDatabase(hashY, AppConsts.AttributeScore, imgY.Value.Score + 1);
            UpdateImgInDatabase(hashY, AppConsts.AttributeLastView, DateTime.Now.Ticks);
        }

        progress?.Report(message);
    }

    public void DeleteRight(IProgress<string>? progress)
    {
        progress?.Report($"Calculating{AppConsts.CharEllipsis}");
        var hashX = _imgPanels[0]!.Value.Hash;
        var hashY = _imgPanels[1]!.Value.Hash;
        var message = GetNext(hashX, hashY);
        var imgX = GetImgFromDatabase(hashX);
        if (imgX != null) {
            UpdateImgInDatabase(hashX, AppConsts.AttributeScore, imgX.Value.Score + 1);
            UpdateImgInDatabase(hashX, AppConsts.AttributeLastView, DateTime.Now.Ticks);
        }

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

        var rvector = CalculateVector(image);
        ChangeVector(hash, rvector);
        UpdateImgInDatabase(hash, AppConsts.AttributeRotateMode, (int)rotatemode);
        UpdateImgInDatabase(hash, AppConsts.AttributeFlipMode, (int)flipmode);
    }

    private bool ImportFile(string orgfilename, ref DateTime lastview, ref int added, ref int found, ref int bad, IProgress<string>? progress)
    {
        var orgname = Path.GetFileNameWithoutExtension(orgfilename);
        var hashByName = orgname.ToLowerInvariant();
        if (AppHash.IsValidHash(hashByName) && ContainsImgInDatabase(hashByName)) {
            var imagedata = AppFile.ReadMex(hashByName);
            if (imagedata == null) {
                var orgimagedata = AppFile.ReadFile(orgfilename);
                if (orgimagedata == null) {
                    AppFile.MoveToRecycleBin(orgfilename);
                    bad++;
                }
                else {
                    AppFile.WriteMex(hashByName, orgimagedata);
                    AppFile.MoveToRecycleBin(orgfilename);
                    found++;
                }
            }
            else {
                AppFile.MoveToRecycleBin(orgfilename);
                found++;
            }
        }
        else {
            var orgimagedata = AppFile.ReadFile(orgfilename);
            if (orgimagedata == null) {
                AppFile.MoveToRecycleBin(orgfilename);
                bad++;
            }
            else {
                var hash = AppHash.GetHash(orgimagedata);
                if (ContainsImgInDatabase(hash)) {
                    AppFile.MoveToRecycleBin(orgfilename);
                    found++;
                }
                else {
                    using var image = AppBitmap.GetImage(orgimagedata);
                    if (image == null) {
                        AppFile.MoveToRecycleBin(orgfilename);
                        bad++;
                    }
                    else {
                        var vector = CalculateVector(image);
                        if (vector == null) {
                            AppFile.MoveToRecycleBin(orgfilename);
                            bad++;
                        }
                        else {
                            var imgnew = new Img {
                                Hash = hash,
                                RotateMode = RotateMode.None,
                                FlipMode = FlipMode.None,
                                LastView = lastview,
                                Score = 0,
                                LastCheck = new DateTime(1980, 1, 1),
                                Next = string.Empty,
                                Distance = 1f,
                                History = string.Empty
                            };

                            AddImgToDatabase(imgnew, vector);
                            AppFile.WriteMex(hash, orgimagedata);
                            AddVector(hash, vector);
                            AppFile.MoveToRecycleBin(orgfilename);
                            added++;
                            var message = GetNext(hash);
                            lastview = lastview.AddMinutes(-1);
                        }
                    }
                }
            }
        }

        progress?.Report($"importing {orgfilename} (a:{added})/f:{found}/b:{bad}){AppConsts.CharEllipsis}");
        return true;
    }

    private void ImportFiles(string path, SearchOption so, ref DateTime lastview, ref int added, ref int found, ref int bad, IProgress<string>? progress)
    {
        var directoryInfo = new DirectoryInfo(path);
        var fs = directoryInfo.GetFiles("*.*", so).ToArray();
        foreach (var e in fs) {
            var orgfilename = e.FullName;
            if (!ImportFile(orgfilename, ref lastview, ref added, ref found, ref bad, progress)) {
                break;
            }

            if (added >= AppConsts.MaxImportFiles) {
                break;
            }
        }

        progress?.Report($"clean-up {path}{AppConsts.CharEllipsis}");
        Helper.CleanupDirectories(path, progress);
    }

    public void Import(IProgress<string>? progress)
    {
        _maxImages -= 100;
        UpdateMaxImagesInDatabase(_maxImages);
        var lastview = GetLastViewFromDatabase()?? DateTime.Now;
        var added = 0;
        var found = 0;
        var bad = 0;
        ImportFiles(AppConsts.PathRawProtected, SearchOption.TopDirectoryOnly, ref lastview, ref added, ref found, ref bad, progress);
        if (added < AppConsts.MaxImportFiles) {
            var directoryInfo = new DirectoryInfo(AppConsts.PathRawProtected);
            var ds = directoryInfo.GetDirectories("*.*", SearchOption.TopDirectoryOnly).ToArray();
            foreach (var di in ds) {
                ImportFiles(di.FullName, SearchOption.AllDirectories, ref lastview, ref added, ref found, ref bad, progress);
                if (added >= AppConsts.MaxImportFiles) {
                    break;
                }
            }
        }

        Helper.CleanupDirectories(AppConsts.PathRawProtected, progress);
        progress?.Report($"Imported a:{added}/f:{found}/b:{bad}");
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

                    _vectorsOwner?.Dispose();
                    _hashToIndex.Clear();
                    Array.Clear(_hashes);
                    _countVectors = 0;
                    _capacityVectors = 0;
                    _sqlConnection?.Dispose();

                    while (_inputDataPool.TryDequeue(out _)) { }
                    while (_vectorPool.TryDequeue(out _)) { }
                    while (_containerPool.TryDequeue(out _)) { }
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
