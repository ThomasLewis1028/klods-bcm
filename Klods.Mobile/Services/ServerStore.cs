using System.Text.Json;
using Klods.Mobile.Models;

namespace Klods.Mobile.Services;

public class ServerStore
{
    private const string Key = "servers";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public List<ServerProfile> GetAll()
    {
        var json = Preferences.Get(Key, null);
        if (json is null) return [];
        return JsonSerializer.Deserialize<List<ServerProfile>>(json, JsonOpts) ?? [];
    }

    public void Upsert(ServerProfile server)
    {
        var list = GetAll();
        var idx = list.FindIndex(s => s.Id == server.Id);
        if (idx >= 0) list[idx] = server;
        else list.Add(server);
        Preferences.Set(Key, JsonSerializer.Serialize(list, JsonOpts));
    }

    public void Remove(string id)
    {
        var list = GetAll().Where(s => s.Id != id).ToList();
        Preferences.Set(Key, JsonSerializer.Serialize(list, JsonOpts));
    }
}
