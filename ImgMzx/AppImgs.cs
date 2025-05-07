using Microsoft.Data.Sqlite;
using System.Data;
using System.Text;
using SixLabors.ImageSharp.Processing;
using System.Windows;

namespace ImgMzx
{
    public static class AppImgs
    {
        private static readonly object _lock = new();
        private static SqliteConnection _sqlConnection = new();
        private static readonly SortedList<string, Img> _imgList = new(); // hash/img
        private static readonly SortedList<string, string> _nameList = new(); // name/hash

        public static string Status { get; private set; } = string.Empty;

        private static string GetSelect()
        {
            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append($"{AppConsts.AttributeHash},"); // 0
            sb.Append($"{AppConsts.AttributeName},"); // 1
            sb.Append($"{AppConsts.AttributeVector},"); // 2
            sb.Append($"{AppConsts.AttributeRotateMode},"); // 3
            sb.Append($"{AppConsts.AttributeFlipMode},"); // 4
            sb.Append($"{AppConsts.AttributeLastView},"); // 5
            sb.Append($"{AppConsts.AttributeVerified},"); // 6
            sb.Append($"{AppConsts.AttributeNext},"); // 7
            sb.Append($"{AppConsts.AttributeDistance},"); // 8
            sb.Append($"{AppConsts.AttributeScore},"); // 9
            sb.Append($"{AppConsts.AttributeLastCheck}"); // 10
            return sb.ToString();
        }

        private static Img Get(IDataRecord reader)
        {
            var hash = reader.GetString(0);
            var name = reader.GetString(1);
            var vector = Helper.ArrayToFloat((byte[])reader[2]);
            var rotatemode = (RotateMode)Enum.Parse(typeof(RotateMode), reader.GetInt64(3).ToString());
            var flipmode = (FlipMode)Enum.Parse(typeof(FlipMode), reader.GetInt64(4).ToString());
            var lastview = DateTime.FromBinary(reader.GetInt64(5));
            var verified = reader.GetBoolean(6);
            var next = reader.GetString(7);
            var distance = reader.GetFloat(8);
            var score = (int)reader.GetInt64(9);
            var lastcheck = DateTime.FromBinary(reader.GetInt64(10));

            var img = new Img(
                hash: hash,
                name: name,
                vector: vector,
                rotatemode: rotatemode,
                flipmode: flipmode,
                lastview: lastview,
                verified: verified,
                next: next,
                distance: distance,
                score: score,
                lastcheck: lastcheck
            );

            return img;
        }

        private static void LoadNamesAndVectors(IProgress<string>? progress)
        {
            var sb = new StringBuilder(GetSelect());
            sb.Append($" FROM {AppConsts.TableImages};");
            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            using var reader = sqlCommand.ExecuteReader();
            if (!reader.HasRows) {
                return;
            }

            var dtn = DateTime.Now;
            while (reader.Read()) {
                var img = Get(reader);
                Add(img);
                if (!(DateTime.Now.Subtract(dtn).TotalMilliseconds > AppConsts.TimeLapse)) {
                    continue;
                }

                dtn = DateTime.Now;
                var count = AppImgs.Count();
                progress?.Report($"Loading names and vectors ({count}){AppConsts.CharEllipsis}");
            }
        }

        private static void LoadVars(IProgress<string>? progress)
        {
            progress?.Report($"Loading vars{AppConsts.CharEllipsis}");

            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append($"{AppConsts.AttributeMaxImages}"); // 0
            sb.Append($" FROM {AppConsts.TableVars};");
            using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
            using var reader = sqlCommand.ExecuteReader();
            if (!reader.HasRows) {
                return;
            }

            while (reader.Read()) {
                AppVars.MaxImages = reader.GetInt32(0);
                break;
            }
        }

        public static void Load(string filedatabase, IProgress<string>? progress)
        {
            lock (_lock) {
                _imgList.Clear();
                _nameList.Clear();
                var connectionString = $"Data Source={filedatabase};";
                _sqlConnection = new SqliteConnection(connectionString);
                _sqlConnection.Open();

                LoadNamesAndVectors(progress);
                LoadVars(progress);
            }
        }

        private static Img? Get(string hash)
        {
            lock (_lock) {
                var sb = new StringBuilder(GetSelect());
                sb.Append(
                    $" FROM {AppConsts.TableImages} WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}");
                using var sqlCommand = new SqliteCommand(sb.ToString(), _sqlConnection);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
                using var reader = sqlCommand.ExecuteReader();
                if (reader.Read()) {
                    var img = Get(reader);
                    return img;
                }
            }

            return null;
        }

        public static void Save(Img img)
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
                    sb.Append($"{AppConsts.AttributeVerified},");
                    sb.Append($"{AppConsts.AttributeNext},");
                    sb.Append($"{AppConsts.AttributeDistance},");
                    sb.Append($"{AppConsts.AttributeScore},");
                    sb.Append($"{AppConsts.AttributeLastCheck}");
                    sb.Append(") VALUES (");
                    sb.Append($"@{AppConsts.AttributeHash},");
                    sb.Append($"@{AppConsts.AttributeName},");
                    sb.Append($"@{AppConsts.AttributeVector},");
                    sb.Append($"@{AppConsts.AttributeRotateMode},");
                    sb.Append($"@{AppConsts.AttributeFlipMode},");
                    sb.Append($"@{AppConsts.AttributeLastView},");
                    sb.Append($"@{AppConsts.AttributeVerified},");
                    sb.Append($"@{AppConsts.AttributeNext},");
                    sb.Append($"@{AppConsts.AttributeDistance},");
                    sb.Append($"@{AppConsts.AttributeScore},");
                    sb.Append($"@{AppConsts.AttributeLastCheck}");
                    sb.Append(')');
                    sqlCommand.CommandText = sb.ToString();
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", img.Hash);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeName}", img.Name);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVector}",
                        Helper.ArrayFromFloat(img.Vector));
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeRotateMode}", (int)img.RotateMode);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeFlipMode}", (int)img.FlipMode);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastView}", img.LastView.Ticks);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVerified}", img.Verified);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeNext}", img.Next);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeDistance}", img.Distance);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeScore}", img.Score);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastCheck}", img.LastCheck.Ticks);
                    sqlCommand.ExecuteNonQuery();
                }

                Add(img);
            }
        }

        public static int Count()
        {
            int count;
            lock (_lock) {
                if (_imgList.Count != _nameList.Count) {
                    throw new Exception();
                }

                count = _imgList.Count;
            }

            return count;
        }

        private static bool ContainsKey(string key)
        {
            bool result;
            lock (_lock) {
                result = key.Length >= 32 ? _imgList.ContainsKey(key) : _nameList.ContainsKey(key);
            }

            return result;
        }

        public static string GetName(string hash)
        {
            string name;
            var length = 5;
            do {
                length++;
                name = hash[..length].ToLower();
            } while (ContainsKey(name));

            return name;
        }

        public static bool TryGet(string hash, out Img? img)
        {
            lock (_lock) {
                return _imgList.TryGetValue(hash, out img);
            }
        }

        public static bool TryGetByName(string name, out Img? img)
        {
            img = null;
            lock (_lock) {
                return _nameList.TryGetValue(name, out var hash) && TryGet(hash, out img);
            }
        }

        private static void Add(Img img)
        {
            lock (_lock) {
                _imgList.Add(img.Hash, img);
                _nameList.Add(img.Name, img.Hash);
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

        public static void Remove(string key)
        {
            lock (_lock) {
                if (key.Length >= 32) {
                    if (TryGet(key, out var img)) {
                        _imgList.Remove(key);
                        if (img != null) {
                            _nameList.Remove(img.Name);
                        }
                    }
                }
                else {
                    if (TryGetByName(key, out var img)) {
                        if (img != null) {
                            _imgList.Remove(img.Hash);
                        }

                        _nameList.Remove(key);
                    }
                }
            }
        }

        private static Img? ImgUpdateProperty(string hash, string key, object val)
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

                return Replace(Get(hash));
            }
        }

        private static Img? Replace(Img? imgnew)
        {
            lock (_lock) {
                if (imgnew != null) {
                    if (ContainsKey(imgnew.Hash)) {
                        Remove(imgnew.Hash);
                    }

                    Add(imgnew);
                }

                return imgnew;
            }
        }

        public static Img? SetVectorFacesOrientation(string hash, float[] vector, RotateMode rotatemode,
            FlipMode flipmode)
        {
            lock (_lock) {
                var sb = new StringBuilder();
                sb.Append($"UPDATE {AppConsts.TableImages} SET ");
                sb.Append($"{AppConsts.AttributeVector} = @{AppConsts.AttributeVector},");
                sb.Append($"{AppConsts.AttributeRotateMode} = @{AppConsts.AttributeRotateMode},");
                sb.Append($"{AppConsts.AttributeFlipMode} = @{AppConsts.AttributeFlipMode} ");
                sb.Append($"WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}");

                using (var sqlCommand = _sqlConnection.CreateCommand()) {
                    sqlCommand.Connection = _sqlConnection;
                    sqlCommand.CommandText = sb.ToString();
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVector}", Helper.ArrayFromFloat(vector));
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeRotateMode}", (int)rotatemode);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeFlipMode}", (int)flipmode);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
                    sqlCommand.ExecuteNonQuery();
                }

                return Replace(Get(hash));
            }
        }

        public static Img? UpdateLastView(string hash)
        {
            return ImgUpdateProperty(hash, AppConsts.AttributeLastView, DateTime.Now.Ticks);
        }

        public static Img? UpdateLastCheck(string hash)
        {
            return ImgUpdateProperty(hash, AppConsts.AttributeLastCheck, DateTime.Now.Ticks);
        }

        public static Img? UpdateVerified(string hash)
        {
            return ImgUpdateProperty(hash, AppConsts.AttributeVerified, true);
        }

        public static Img? SetScore(string hash, int score)
        {
            return ImgUpdateProperty(hash, AppConsts.AttributeScore, score);
        }

        public static Img? SetNextDistance(string hash, string next, float distance)
        {
            lock (_lock) {
                var sb = new StringBuilder();
                sb.Append($"UPDATE {AppConsts.TableImages} SET ");
                sb.Append($"{AppConsts.AttributeNext} = @{AppConsts.AttributeNext},");
                sb.Append($"{AppConsts.AttributeDistance} = @{AppConsts.AttributeDistance} ");
                sb.Append($"WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}");

                using (var sqlCommand = _sqlConnection.CreateCommand()) {
                    sqlCommand.Connection = _sqlConnection;
                    sqlCommand.CommandText = sb.ToString();
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeNext}", next);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeDistance}", distance);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
                    sqlCommand.ExecuteNonQuery();
                }

                return Replace(Get(hash));
            }
        }

        public static Img? SetRadius(string hash, float radius)
        {
            return ImgUpdateProperty(hash, AppConsts.AttributeRadius, radius);
        }

        public static Img? GetForView()
        {
            var list = new List<Img>();
            lock (_lock) {
                foreach (var img in _imgList.Values) {
                    if (!_imgList.TryGetValue(img.Next, out var imgnext)) {
                        continue;
                    }

                    list.Add(img);
                }
            }

            var groupedData = list
                .GroupBy(x => new { x.Verified, x.Score })
                .OrderBy(g => g.Key.Verified)
                .ThenBy(g => g.Key.Score)
                .Select(g => new {
                    GroupKey = g.Key,
                    OrderedItems = g.ToList()
                })
                .ToArray();

            if (!groupedData.Any()) {
                return null;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < 3; i++) {
                if(i >= groupedData.Length) {
                    break;
                }

                if (i > 0) {
                    sb.Append("/");
                }

                sb.Append(groupedData[i].OrderedItems[0].Verified ? "" : "n");
                sb.Append(groupedData[i].OrderedItems[0].Score);
                sb.Append(':');
                sb.Append(groupedData[i].OrderedItems.Count);
            }

            sb.Append($"/{AppConsts.CharEllipsis}/");
            lock (_lock) {
                var diff = _imgList.Count - AppVars.MaxImages;
                sb.Append($"{_imgList.Count} ({diff})");
            }

            list = list
                .OrderBy(e => e.Verified)
                .ThenBy(e => e.Score)
                .Take(10000)
                .ToList();

            var w = new double[list.Count];
            var wsum = 0.0;
            var maxlv = list.Max(e => e.LastView.Ticks / (double)TimeSpan.TicksPerDay);
            for (var i = 0; i < list.Count; i++) {
                var diff = maxlv - (list[i].LastView.Ticks / (double)TimeSpan.TicksPerDay);
                w[i] = diff * diff;
                wsum += w[i];
            }

            for (var i = 0; i < list.Count; i++) {
                w[i] /= wsum;
            }

            var random = AppVars.RandomDouble();
            var a = 0;
            for (a = 0; a < list.Count; a++) {
                if (random < w[a]) {
                    break;
                }

                random -= w[a];
            }

            sb.Append($" a:{a}");
            Status = sb.ToString();

            var imgX = list[a];

            /*
            Img[] recent;
            lock (_lock) {
                recent = _imgList.Values.OrderByDescending(e => e.LastView).Take(256).ToArray();
            }
            */

            /*
            var minVerified = list.Min(e => e.Verified);
            list = list.Where(e => e.Verified == minVerified).ToList();
            var minScore = list.Min(e => e.Score);
            list = list.Where(e => e.Score == minScore).ToList();
            */

            /*
            list = list
                .OrderBy(e => e.Verified)
                .ThenBy(e => e.Score)
                .Take(10000)
                .ToList();
            var distances = new float[list.Count, recent.Length];
            Parallel.For(0, list.Count, i => {
                var img = list[i];
                Parallel.For(0, recent.Length, j => { distances[i, j] = AppVit.GetDistance(img.Vector, recent[j].Vector); });
            });

            var imgX = list[0];
            var maxDistance = 0f;
            for (var i = 0; i < list.Count; i++) {
                var minDistance = distances[i, 0];
                for (var j = 1; j < recent.Length; j++) {
                    if (distances[i, j] < minDistance) {
                        minDistance = distances[i, j];
                    }
                }
                if (minDistance > maxDistance) {
                    maxDistance = minDistance;
                    imgX = list[i];
                }
            }
            */


            /*
            var minVerified = list.Min(x => x.Verified); 
            list = list.Where(x => x.Verified == minVerified).ToList();
            var minScore = list.Min(x => x.Score);
            list = list.Where(x => x.Score == minScore).ToList();

            var imgX = list.MinBy(e => e.LastView.AddMinutes(AppVars.GetRandomIndex(60*24*7)));
            */
            /*
            var distances = new float[list.Count];
            Parallel.For(0, distances.Length, i => { distances[i] = AppVit.GetDistance(_lastVector, list[i].Vector); });

            // find i where distances[i] is minimal

            var minIndex = 0;
            var minDistance = distances[0];
            for (var i = 1; i < distances.Length; i++) {
                if (distances[i] < minDistance) {
                    minDistance = distances[i];
                    minIndex = i;
                }
            }

            var imgX = list[minIndex];
            Array.Copy(list[minIndex].Vector, _lastVector, list[minIndex].Vector.Length);
            */

            /*
            var imgX = list[0];
            for (var a = 0; a < 10000; a++) {
                var randomIndex = AppVars.GetRandomIndex(list.Count);
                var img = list[randomIndex];
                if (imgX.Verified && !img.Verified) {
                    imgX = img;
                }
                else if (!imgX.Verified && img.Verified) {
                    continue;
                }
                else if (imgX.Score > img.Score) {
                    imgX = img;
                }
                else if (imgX.Score < img.Score) {
                    continue;
                }
                else if (imgX.LastView > img.LastView) {
                    imgX = img;
                }
            }
            */

            return imgX;
        }

        public static Img GetForCheck()
        {
            lock (_lock) {
                foreach (var img in _imgList.Values) {
                    if (img.Hash.Equals(img.Next) || !_imgList.ContainsKey(img.Next)) {
                        return img;
                    }
                }

                return _imgList
                    .MinBy(e => e.Value.LastCheck)
                    .Value;
            }
        }

        public static Tuple<Img, float>[] GetBeam(Img img)
        {
            List<Img> shadow;
            lock (_lock) {
                shadow = _imgList.Values.ToList();
                var imgD = shadow.FirstOrDefault(e => e.Hash.Equals(img.Hash));
                if (imgD != null) {
                    shadow.Remove(imgD);
                }
                else {
                    return Array.Empty<Tuple<Img, float>>();
                }
            }

            var distances = new float[shadow.Count];
            var vx = img.Vector;
            Parallel.For(0, distances.Length, i => { distances[i] = AppVit.GetDistance(vx, shadow[i].Vector); });

            var beam = shadow
                .Zip(distances, Tuple.Create)
                .OrderBy(t => t.Item2)
                .ToArray();

            return beam;
        }

        public static DateTime GetMinimalLastView()
        {
            lock (_lock) {
                return _imgList.Min(e => e.Value.LastView).AddSeconds(-1);
            }
        }

        public static void AddHistory(string hashx, string hashy)
        {
            const string commandtext =
                $"INSERT INTO {AppConsts.TablePairs} ({AppConsts.AttributeHashX}, {AppConsts.AttributeHashY}) VALUES (@{AppConsts.AttributeHashX}, @{AppConsts.AttributeHashY})";
            lock (_lock) {
                lock (_lock) {
                    using var sqlCommand = _sqlConnection.CreateCommand();
                    sqlCommand.Connection = _sqlConnection;
                    sqlCommand.CommandText = commandtext;
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHashX}", hashx);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHashY}", hashy);
                    sqlCommand.ExecuteNonQuery();
                }
            }
        }

        public static SortedSet<string> GetHistory(string hashX)
        {
            var set = new SortedSet<string>();
            const string commandtext =
                $"SELECT {AppConsts.AttributeHashY} FROM {AppConsts.TablePairs} WHERE {AppConsts.AttributeHashX} = @{AppConsts.AttributeHashX}";
            lock (_lock) {
                using var sqlCommand = new SqliteCommand(commandtext, _sqlConnection);
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHashX}", hashX);
                using var reader = sqlCommand.ExecuteReader();
                while (reader.Read()) {
                    var hashy = reader.GetString(0);
                    set.Add(hashy);
                }
            }

            return set;
        }

        public static void DeleteHistory(string hash)
        {
            const string commandtext =
                $"DELETE FROM {AppConsts.TablePairs} WHERE {AppConsts.AttributeHashX} = @{AppConsts.AttributeHash} OR {AppConsts.AttributeHashY} = @{AppConsts.AttributeHash}";
            lock (_lock) {
                lock (_lock) {
                    using var sqlCommand = _sqlConnection.CreateCommand();
                    sqlCommand.Connection = _sqlConnection;
                    sqlCommand.CommandText = commandtext;
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
                    sqlCommand.ExecuteNonQuery();
                }
            }
        }

        public static void UpdateMaxImages()
        {
            lock (_lock) {
                AppVars.MaxImages = (int)Math.Round(AppVars.MaxImages * 0.9996);
                using var sqlCommand = _sqlConnection.CreateCommand();
                sqlCommand.Connection = _sqlConnection;
                sqlCommand.CommandText =
                    $"UPDATE {AppConsts.TableVars} SET {AppConsts.AttributeMaxImages} = @{AppConsts.AttributeMaxImages}";
                sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeMaxImages}", AppVars.MaxImages);
                sqlCommand.ExecuteNonQuery();
            }
        }
    }
}
