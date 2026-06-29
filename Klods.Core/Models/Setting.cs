using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

/// <summary>Simple key/value app settings store (e.g. the RSS auto-update toggle + last-processed pubDate).</summary>
[Table("Settings")]
public class Setting
{
    public string Key { get; set; }

    public string Value { get; set; }
}
