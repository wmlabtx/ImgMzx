using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp.Processing;
using System.Runtime.InteropServices;

namespace ImgMzx;

public partial class Images : IDisposable
{
    public int GetCount()
    {
        lock (_lock) {
            using var command = new SqliteCommand(
                $"SELECT COUNT(*) FROM {AppConsts.TableImages}", 
                _sqlConnection);
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    public bool ContainsImg(string hash)
    {
        lock (_lock) {
            using var command = new SqliteCommand(
                $"SELECT COUNT({AppConsts.AttributeHash}) FROM {AppConsts.TableImages} WHERE {AppConsts.AttributeHash} = @hash",
                _sqlConnection);
            command.Parameters.AddWithValue("@hash", hash);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }
    }

    public void UpdateVectorInDatabase(string hash, ReadOnlySpan<float> vector)
    {
        lock (_lock) {
            using var sqlCommand = new SqliteCommand(
                $"UPDATE {AppConsts.TableImages} SET {AppConsts.AttributeVector} = @value WHERE {AppConsts.AttributeHash} = @hash",
                _sqlConnection);
            sqlCommand.Parameters.AddWithValue("@value", MemoryMarshal.Cast<float, byte>(vector).ToArray());
            sqlCommand.Parameters.AddWithValue("@hash", hash);
            sqlCommand.ExecuteNonQuery();
        }
    }

    public void UpdateImgInDatabase(string hash, string key, object val)
    {
        lock (_lock) {
            // Single choke point for every column write, so the in-memory mirror
            // used by PickNextSubject/GetNext cannot drift away from the database.
            if (_hashToIndex.TryGetValue(hash, out var cachedSlot)) {
                if (key == AppConsts.AttributeLastView) {
                    _lastViewTicks[cachedSlot] = Convert.ToInt64(val);
                }
                else if (key == AppConsts.AttributeHistory) {
                    _historyLength[cachedSlot] = ((string?)val)?.Length ?? 0;
                }
                else if (key == AppConsts.AttributeRate) {
                    _rate[cachedSlot] = Convert.ToInt32(val);
                }
            }

            using var sqlCommand = new SqliteCommand(
                $"UPDATE {AppConsts.TableImages} SET {key} = @value WHERE {AppConsts.AttributeHash} = @hash",
                _sqlConnection);
            sqlCommand.Parameters.AddWithValue("@value", val ?? DBNull.Value);
            sqlCommand.Parameters.AddWithValue("@hash", hash);
            sqlCommand.ExecuteNonQuery();
        }
    }

    public void AddImgToDatabase(Img img, Span<float> vector)
    {
        lock (_lock) {
            AddVector(img.Hash, vector, img.LastView.Ticks, img.History.Length, img.Rate);
            using var sqlCommand = _sqlConnection.CreateCommand();
            sqlCommand.CommandText = $@"
            INSERT INTO {AppConsts.TableImages} (
                {AppConsts.AttributeHash},
                {AppConsts.AttributeRotateMode},
                {AppConsts.AttributeFlipMode},
                {AppConsts.AttributeLastView},
                {AppConsts.AttributeHistory},
                {AppConsts.AttributeVector},
                {AppConsts.AttributeRate}
            ) VALUES (
                @{AppConsts.AttributeHash},
                @{AppConsts.AttributeRotateMode},
                @{AppConsts.AttributeFlipMode},
                @{AppConsts.AttributeLastView},
                @{AppConsts.AttributeHistory},
                @{AppConsts.AttributeVector},
                @{AppConsts.AttributeRate}
            );";
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", img.Hash);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeRotateMode}", (int)img.RotateMode);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeFlipMode}", (int)img.FlipMode);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeLastView}", img.LastView.Ticks);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHistory}", img.History);
            var vectorBytes = MemoryMarshal.Cast<float, byte>(GetVector(img.Hash)).ToArray();
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeVector}", vectorBytes);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeRate}", img.Rate);
            sqlCommand.ExecuteNonQuery();

            MaxImages--;
        }
    }

    public Img GetImgFromDatabase(string hash)
    {
        lock (_lock) {
            var sql = $@"
            SELECT
                {AppConsts.AttributeHash},
                {AppConsts.AttributeRotateMode},
                {AppConsts.AttributeFlipMode},
                {AppConsts.AttributeLastView},
                {AppConsts.AttributeHistory},
                {AppConsts.AttributeRate}
            FROM {AppConsts.TableImages}
            WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash};";
            using var sqlCommand = new SqliteCommand(sql, _sqlConnection);
            sqlCommand.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
            using var reader = sqlCommand.ExecuteReader();
            if (reader.Read()) {
                return new Img(
                    hash: reader.GetString(0),
                    rotateMode: Enum.Parse<RotateMode>(reader.GetInt64(1).ToString()),
                    flipMode: Enum.Parse<FlipMode>(reader.GetInt64(2).ToString()),
                    lastView: new DateTime(reader.GetInt64(3)),
                    history: reader.GetString(4),
                    rate: reader.GetInt32(5),
                    images: this);
            }

            return new Img(
                hash: string.Empty,
                rotateMode: RotateMode.None,
                flipMode: FlipMode.None,
                lastView: DateTime.MinValue,
                history: string.Empty,
                rate: 0,
                images: this);
        }
    }

    public void DeleteImgInDatabase(string hash)
    {
        lock (_lock) {
            RemoveVector(hash);
            using var command = new SqliteCommand(
                $"DELETE FROM {AppConsts.TableImages} WHERE {AppConsts.AttributeHash} = @{AppConsts.AttributeHash}",
                _sqlConnection);
            command.Parameters.Clear();
            command.Parameters.AddWithValue($"@{AppConsts.AttributeHash}", hash);
            command.ExecuteNonQuery();
        }
    }

    public int GetMinHistoryCount()
    {
        lock (_lock) {
            using var command = new SqliteCommand(
                $@"SELECT COUNT(*) FROM {AppConsts.TableImages} WHERE LENGTH({AppConsts.AttributeHistory}) = (SELECT MIN(LENGTH({AppConsts.AttributeHistory})) FROM {AppConsts.TableImages});",
                _sqlConnection);
            var result = command.ExecuteScalar();
            return Convert.ToInt32(result);
        }
    }

    /*
    public string PickNextSubject()
    {
        lock (_lock) {
            using var command = new SqliteCommand(
                $@"SELECT {AppConsts.AttributeHash} FROM {AppConsts.TableImages}
                   WHERE length({AppConsts.AttributeHistory}) = (
                       SELECT length({AppConsts.AttributeHistory}) FROM {AppConsts.TableImages}
                       GROUP BY length({AppConsts.AttributeHistory})
                        ORDER BY 1
                       LIMIT 1)
                   ORDER BY RANDOM()
                   LIMIT 1;",
                _sqlConnection);
            return command.ExecuteScalar() as string ?? string.Empty;
        }
    }
    */

    /// <summary>
    /// Picks an image with a probability proportional to how long ago it was seen:
    /// weight(row) = (maxLastView - lastView) + 1, so the least recently viewed rows
    /// get the largest share and a freshly viewed row still keeps a non-zero chance.
    /// Weights are measured in seconds rather than ticks to keep the running sum far
    /// away from long overflow (see PickNextSubjectWeight). Rated rows then have their
    /// weight multiplied by PickNextSubjectBoost so they come up far more often, in the
    /// ratio set by AppConsts.UnratedPerRated.
    /// </summary>
    public string PickNextSubject()
    {
        lock (_lock) {
            if (_hashToIndex.Count == 0) {
                return string.Empty;
            }

            var maxLastViewTicks = long.MinValue;
            foreach (var slot in _hashToIndex.Values) {
                if (_lastViewTicks[slot] > maxLastViewTicks) {
                    maxLastViewTicks = _lastViewTicks[slot];
                }
            }

            // First pass: total weight, split by rate so the boost can be sized against
            // what the rated rows actually weigh right now. Sorting the rows is not
            // required - walking the weights in any order yields the same distribution.
            var ratedsum = 0L;
            var unratedsum = 0L;
            foreach (var slot in _hashToIndex.Values) {
                var weight = PickNextSubjectWeight(maxLastViewTicks, _lastViewTicks[slot]);
                if (_rate[slot] > 0) {
                    ratedsum += weight;
                }
                else {
                    unratedsum += weight;
                }
            }

            var boost = PickNextSubjectBoost(ratedsum, unratedsum);
            var lvsum = unratedsum + (ratedsum * boost);
            if (lvsum <= 0) {
                return string.Empty;
            }

            // Second pass: find the row the random point falls into.
            var point = Random.Shared.NextInt64(lvsum);
            foreach (var slot in _hashToIndex.Values) {
                var weight = PickNextSubjectWeight(maxLastViewTicks, _lastViewTicks[slot]);
                if (_rate[slot] > 0) {
                    weight *= boost;
                }

                point -= weight;
                if (point < 0) {
                    return _slotToHash[slot];
                }
            }

            return string.Empty;
        }
    }

    private static long PickNextSubjectWeight(long maxLastViewTicks, long lastViewTicks)
    {
        // Seconds, not ticks: a tick-based weight is up to 10^7 times larger, and with
        // a large library spanning years the sum would run into long overflow.
        var seconds = (maxLastViewTicks - lastViewTicks) / TimeSpan.TicksPerSecond;
        return seconds < 0 ? 1 : seconds + 1;
    }

    /// <summary>
    /// Multiplier applied to a rated row's weight. Solving
    /// boost * ratedsum = unratedsum / UnratedPerRated gives the boost below, which puts
    /// the rated rows at 1 / (UnratedPerRated + 1) of the total weight - one rated
    /// subject per UnratedPerRated unrated ones - no matter how many images are rated.
    /// Never less than 1: once the rated rows already carry more than their share, they
    /// are left alone rather than suppressed. The boosted total is at most
    /// unratedsum * (1 + 1 / UnratedPerRated), so it cannot overflow long.
    /// </summary>
    private static long PickNextSubjectBoost(long ratedsum, long unratedsum)
    {
        if (ratedsum <= 0) {
            return 1;
        }

        var boost = unratedsum / (AppConsts.UnratedPerRated * ratedsum);
        return boost < 1 ? 1 : boost;
    }

    public DateTime GetLastView()
    {
        lock (_lock) {
            using var command = new SqliteCommand(
                $@"SELECT MIN({AppConsts.AttributeLastView}) FROM {AppConsts.TableImages};",
                _sqlConnection);
            var result = command.ExecuteScalar();
            var ticks = Convert.ToInt64(result);
            return new DateTime(ticks);
        }
    }

    public string? FindClosest(float[] vector)
    {
        lock (_lock) {
            // _hashToIndex holds only live slots, so free slots need no filtering.
            var minHistoryLength = int.MaxValue;
            foreach (var slot in _hashToIndex.Values) {
                if (_historyLength[slot] < minHistoryLength) {
                    minHistoryLength = _historyLength[slot];
                }
            }

            var slotArray = _hashToIndex.Values
                .Where(slot => _historyLength[slot] == minHistoryLength)
                .ToArray();
            if (slotArray.Length == 0) {
                return null;
            }

            var distances = new float[slotArray.Length];
            Parallel.For(0, slotArray.Length, i => {
                var v = _vectors.AsSpan(slotArray[i] * AppConsts.VectorSize, AppConsts.VectorSize);
                distances[i] = Vit.ComputeDistance(vector, v);
            });

            var best = 0;
            for (var i = 1; i < distances.Length; i++) {
                if (distances[i] < distances[best]) {
                    best = i;
                }
            }

            return _slotToHash[slotArray[best]];
        }
    }
}
