using System.Text.Json.Nodes;
using CentBrowser_PortableAssistant.Core.Backup;

namespace CentBrowser_PortableAssistant.Tests;

public class JsonTrimmerTests
{
    [Fact]
    public void LocalStateDropsMachineKeyAndUnselectedProfiles()
    {
        var node = JsonNode.Parse("""
            {
              "os_crypt": { "encrypted_key": "secret" },
              "browser": { "x": 1 },
              "profile": {
                "info_cache": { "Profile 1": { "name": "a" }, "Profile 2": { "name": "b" } },
                "profiles_order": ["Profile 2", "Profile 1"],
                "last_active_profiles": ["Profile 2"],
                "last_used": "Profile 2"
              }
            }
            """)!;

        var result = JsonTrimmer.TrimLocalState(node, ["Profile 1"], "5.3.1184.31").AsObject();

        Assert.False(result.ContainsKey("os_crypt"));
        Assert.Equal("5.3.1184.31", result["browser"]!["suppress_default_browser_prompt_for_version"]!.GetValue<string>());
        Assert.Equal(1, result["browser"]!["x"]!.GetValue<int>());
        var profile = result["profile"]!;
        Assert.Equal(["Profile 1"], profile["info_cache"]!.AsObject().Select(kv => kv.Key));
        Assert.Equal("Profile 1", profile["last_used"]!.GetValue<string>());
        Assert.Single(profile["profiles_order"]!.AsArray());
        Assert.Empty(profile["last_active_profiles"]!.AsArray());
        Assert.Contains("\"profiles_order\":[\"Profile 1\"]", result.ToJsonString(new System.Text.Json.JsonSerializerOptions()));
    }

    [Fact]
    public void LocalStateKeepsMachineKeyWhenAsked()
    {
        var node = JsonNode.Parse("""{ "os_crypt": { "encrypted_key": "secret" } }""")!;
        var result = JsonTrimmer.TrimLocalState(node, ["Profile 1"], "1.2.3.4", keepOsCrypt: true).AsObject();
        Assert.Equal("secret", result["os_crypt"]!["encrypted_key"]!.GetValue<string>());
    }

    private static JsonNode Prefs() => JsonNode.Parse("""
        {
          "homepage": "https://example.com",
          "extensions": { "settings": {
            "keep": { "location": 1 },
            "drop": { "location": 1 },
            "pdf": { "location": 5 }
          } },
          "protection": { "macs": { "extensions": { "settings": { "keep": "m1", "drop": "m2" } } } }
        }
        """)!;

    [Fact]
    public void PreferencesDropUnselectedExtensionsButKeepComponents()
    {
        var result = JsonTrimmer.TrimPreferences(Prefs(), new HashSet<string> { "keep" }, settingsIncluded: true);

        var ids = result["extensions"]!["settings"]!.AsObject().Select(kv => kv.Key).ToList();
        Assert.Equal(["keep", "pdf"], ids);
        Assert.Null(result["protection"]!["macs"]!["extensions"]!["settings"]!["drop"]);
        Assert.Equal("https://example.com", result["homepage"]!.GetValue<string>());
    }

    [Fact]
    public void WithoutSettingsOnlyExtensionRegistrationRemains()
    {
        var result = JsonTrimmer.TrimPreferences(Prefs(), new HashSet<string> { "keep" }, settingsIncluded: false).AsObject();

        Assert.Equal(["extensions"], result.Select(kv => kv.Key));
        Assert.NotNull(result["extensions"]!["settings"]!["keep"]);
    }
}
