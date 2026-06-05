namespace Klods.Services;

public record UserInfo(
    int UserId,
    string UserName,
    string Role,
    string? ProfilePictureUrl,
    string? PrimaryColor,
    bool HasPassword);
