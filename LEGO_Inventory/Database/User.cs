namespace LEGO_Inventory.Database;

public class User
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }
}
