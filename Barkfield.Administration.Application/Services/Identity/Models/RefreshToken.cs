namespace Barkfield.Administration.Application.Services.Identity.Models;

/// <summary>
/// A refresh token as stored. The raw token is never persisted — only its SHA-256 hash.
/// </summary>
/// <remarks>
/// Property names mirror the RefreshTokens table exactly, because Dapper binds both the
/// INSERT parameters and the SELECT results by name. A column present in the SQL but
/// missing here fails at runtime with "must declare the scalar variable", which is why the
/// IP and revocation-reason fields are carried even though they are often null.
/// </remarks>
public class RefreshToken
{
    public int Id { get; set; }
    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Client address the session was issued to, for investigating token theft.</summary>
    public string? CreatedByIp { get; set; }

    public DateTime? RevokedAt { get; set; }
    public string? RevokedByIp { get; set; }

    /// <summary>Hash of the token that superseded this one, when rotated.</summary>
    public string? ReplacedByTokenHash { get; set; }

    public string? ReasonRevoked { get; set; }

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}
