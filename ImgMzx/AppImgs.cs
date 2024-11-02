using Microsoft.Data.Sqlite;
using System.Data;
using System.Text;
using SixLabors.ImageSharp.Processing;

namespace ImgMzx
{
    public static class AppImgs
    {
        private static readonly object _lock = new();
        private static SqliteConnection _sqlConnection = new();
        private static readonly SortedList<string, Img> _imgList = new(); // hash/img
        private static readonly SortedList<string, string> _nameList = new(); // name/hash

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
            sb.Append($"{AppConsts.AttributeHorizon},"); // 7
            sb.Append($"{AppConsts.AttributeCounter},"); // 8
            sb.Append($"{AppConsts.AttributeNodes}"); // 9
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
            var horizon = reader.GetString(7);
            var counter = (int)reader.GetInt64(8);
            var nodes = reader.GetString(9);

            var img = new Img(
                hash: hash,
                name: name,
                vector: vector,
                rotatemode: rotatemode,
                flipmode: flipmode,
                lastview: lastview,
                verified: verified,
                horizon: horizon,
                counter: counter,
                nodes: nodes
            );

            return img;
        }

        public static void Load(string filedatabase, IProgress<string>? progress)
        {
            lock (_lock) {
                _imgList.Clear();
                _nameList.Clear();
                var connectionString = $"Data Source={filedatabase};";
                _sqlConnection = new SqliteConnection(connectionString);
                _sqlConnection.Open();

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
        }

        private static Img? Get(string hash)
        {
            lock (_lock) {
                var sb = new StringBuilder(GetSelect());
                sb.Append($" FROM {AppConsts.TableImages} WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}");
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
                    sb.Append($"{AppConsts.AttributeHorizon},");
                    sb.Append($"{AppConsts.AttributeCounter},");
                    sb.Append($"{AppConsts.AttributeNodes}");
                    sb.Append(") VALUES (");
                    sb.Append($"@{AppConsts.AttributeHash},");
                    sb.Append($"@{AppConsts.AttributeName},");
                    sb.Append($"@{AppConsts.AttributeVector},");
                    sb.Append($"@{AppConsts.AttributeRotateMode},");
                    sb.Append($"@{AppConsts.AttributeFlipMode},");
                    sb.Append($"@{AppConsts.AttributeLastView},");
                    sb.Append($"@{AppConsts.AttributeVerified},");
                    sb.Append($"@{AppConsts.AttributeHorizon},");
                    sb.Append($"@{AppConsts.AttributeCounter},");
                    sb.Append($"@{AppConsts.AttributeNodes}");
                    sb.Append(')');
                    sqlCommand.CommandText = sb.ToString();
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", img.Hash);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeName}", img.Name);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVector}", Helper.ArrayFromFloat(img.Vector));
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeRotateMode}", (int)img.RotateMode);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeFlipMode}", (int)img.FlipMode);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastView}", img.LastView.Ticks);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVerified}", img.Verified);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHorizon}", img.Horizon);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeCounter}", img.Counter);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeNodes}", img.Nodes);
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
                result = key.Length >= 32 ? 
                    _imgList.ContainsKey(key) : 
                    _nameList.ContainsKey(key);
            }

            return result;
        }

        public static string GetName(string hash)
        {
            string name;
            var length = 5;
            do {
                length++;
                name = hash.Substring(0, length).ToLower();
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

        public static Img? SetVectorFacesOrientation(string hash, float[] vector, RotateMode rotatemode, FlipMode flipmode)
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

        public static Img? UpdateVerified(string hash)
        {
            return ImgUpdateProperty(hash, AppConsts.AttributeVerified, true);
        }

        public static Img? SetHorizonCounterNodes(string hash, string horizon, int counter, string nodes)
        {
            lock (_lock) {
                var sb = new StringBuilder();
                sb.Append($"UPDATE {AppConsts.TableImages} SET ");
                sb.Append($"{AppConsts.AttributeHorizon} = @{AppConsts.AttributeHorizon},");
                sb.Append($"{AppConsts.AttributeCounter} = @{AppConsts.AttributeCounter},");
                sb.Append($"{AppConsts.AttributeNodes} = @{AppConsts.AttributeNodes} ");
                sb.Append($"WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}");

                using (var sqlCommand = _sqlConnection.CreateCommand()) {
                    sqlCommand.Connection = _sqlConnection;
                    sqlCommand.CommandText = sb.ToString();
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHorizon}", horizon);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeCounter}", counter);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeNodes}", nodes);
                    sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
                    sqlCommand.ExecuteNonQuery();
                }

                return Replace(Get(hash));
            }
        }

        public static Img GetForView()
        {
            lock (_lock) {
                if (AppVars.RandomNext(2) > 0) {
                    var scope = _imgList.Where(e => e.Value.Horizon.Length > 0).ToArray();
                    if (scope.Length > 0) {
                        return scope
                            .MinBy(e => e.Value.Horizon)
                            .Value;
                    }
                }

                var scope_zero = _imgList.Where(e => e.Value.Horizon.Length == 0).ToArray();
                var rindex = AppVars.RandomNext(scope_zero.Length);
                return scope_zero[rindex].Value;

                /*
                var rindex = AppVars.RandomNext(1000);
                if (AppVars.RandomNext(2) > 0) {
                    return _imgList
                        .OrderBy(e => e.Value.LastView)
                        .Skip(rindex)
                        .First()
                        .Value;
                }

                return _imgList
                    .OrderByDescending(e => e.Value.LastView)
                    .Skip(1000)
                    .First()
                    .Value;
                */
            }
        }

        public static Img GetForCheck()
        {
            lock (_lock) {
                foreach (var img in _imgList.Values) {
                    if (img.Hash.Length <= 32) {
                        return img;
                    }

                    if (img is { Counter: > 0, Nodes.Length: <= 32  }) {
                        return img;
                    }

                    if (img.Vector.Length != 512) {
                        return img;
                    }
                }

                var rindex = AppVars.RandomNext(_imgList.Count);
                var imgX = _imgList.ElementAt(rindex).Value;
                return imgX;
            }
        }

        public static List<string> GetBeam(Img img)
        {
            lock (_lock) {
                var hashList = new List<string>();
                var vectorList = new List<float[]>();
                foreach (var e in _imgList.Values) {
                    if (e.Hash.Equals(img.Hash)) {
                        continue;
                    }

                    hashList.Add(e.Hash);
                    vectorList.Add(e.Vector);
                }

                var distances = new float[hashList.Count];
                var vx = img.Vector;
                Parallel.For(0, distances.Length, i => {
                    distances[i] = AppVit.GetDistance(vx, vectorList[i]);
                });

                var vector = hashList.Select((t, i) => Helper.GetHashDistance(t, distances[i])).ToList();
                vector.Sort();
                return vector;
            }
        }

        public static DateTime GetMinimalLastView()
        {
            lock (_lock) {
                return _imgList.Min(e => e.Value.LastView).AddSeconds(-1);
            }
        }
    }
}
