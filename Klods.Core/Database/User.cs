using System.ComponentModel.DataAnnotations;

namespace Klods.Database;

public class User
{
    public const int MaxUserNameLength = 40;

    public int UserId { get; set; }

    [MaxLength(MaxUserNameLength)]
    public string UserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? MascotVariant { get; set; }
    public string? BodyStyle { get; set; }
    public double FontScale { get; set; } = 1.0;
    public bool HasSeenTour { get; set; }
    public string Role { get; set; } = "User";
    public string Status { get; set; } = "Active";
}
