using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.InvidiousChannel;

public class Plugin : BasePlugin<PluginConfiguration>
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Invidious Channel";

    // Stable GUID — do not change after first install
    public override Guid Id => Guid.Parse("4a5b6c7d-8e9f-0a1b-2c3d-4e5f6a7b8c9d");

    public override string Description => "Browse YouTube through your local Invidious instance";
}
