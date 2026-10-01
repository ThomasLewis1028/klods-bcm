using System.ComponentModel.DataAnnotations;

namespace Klods.Database;

/// <summary>
/// A per-user bearer key for the MCP server. Only the SHA-256 of the key is stored; the plaintext is
/// shown once at creation. Keys never expire — revoking one deletes the row.
/// </summary>
public class ApiKey
{
    public const int MaxNameLength = 50;

    public int Id { get; set; }
    public int UserId { get; set; }

    [MaxLength(MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Leading characters of the key, kept so users can tell their keys apart.</summary>
    [MaxLength(16)]
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Lowercase hex SHA-256 of the full key.</summary>
    [MaxLength(64)]
    public string Hash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
