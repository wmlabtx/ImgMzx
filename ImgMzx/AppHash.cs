using System.Security.Cryptography;
using System.Text;

namespace ImgMzx;

public static class AppHash
{
    public static string GetHash(byte[] data)
    {
        var buffer = SHA256.HashData(data);
        var hash = Convert.ToHexString(buffer);
        return hash;
    }

    public static void GetHorizon(List<string> beam, int position, out string horizon, out int counter, out string nodes, out string distance)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < position; i++) {
            sb.Append(beam[position]);
        }

        counter = position;
        horizon = position > 0 ? beam[position - 1] : string.Empty;
        nodes = sb.Length > 0 ? GetHash(Encoding.ASCII.GetBytes(sb.ToString())) : string.Empty;
        distance = beam[position][..4];
    }
}