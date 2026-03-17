using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.InvidiousChannel;

public class PluginConfiguration : BasePluginConfiguration
{
    public PluginConfiguration()
    {
        InvidiousUrl = "http://invidious.lan";
        ApiToken = string.Empty;
    }

    /// <summary>
    /// Base URL of your Invidious instance (no trailing slash).
    /// Default matches your local setup at http://invidious.lan.
    /// </summary>
    public string InvidiousUrl { get; set; }

    /// <summary>
    /// Optional Invidious session cookie value for authenticated requests
    /// (subscriptions feed). Leave blank for anonymous use.
    /// To get this: log into Invidious, open DevTools → Application → Cookies,
    /// copy the value of the "SID" cookie.
    /// </summary>
    public string ApiToken { get; set; }
}
