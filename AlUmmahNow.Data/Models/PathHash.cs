using System.Security.Cryptography;
using System.Text;

namespace AlUmmahNow.Data.Models
{
    /// <summary>
    /// Fixed-length key for a file path, for unique indexes on paths longer than an index key allows.
    /// </summary>
    /// <remarks>
    /// Uppercase hex SHA-256 of the UTF-16LE bytes, which is exactly what SQL Server computes for an
    /// <c>nvarchar</c> with <c>CONVERT(nvarchar(64), HASHBYTES('SHA2_256', col), 2)</c>, so existing rows can be
    /// backfilled in SQL (see <c>ImportDbSchema</c>).
    /// </remarks>
    public static class PathHash
    {
        public const int Length = 64;

        public static string Of(string path) => Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(path ?? string.Empty)));
    }
}
