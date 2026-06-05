namespace LEGO_Inventory.Database;

public class UserExternalLogin
{
    public int UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
}
