using Jellyfin.Plugin.YouTubeFeed.Api;
using YouTubeFeedChannelImpl = Jellyfin.Plugin.YouTubeFeed.Channel.YouTubeFeedChannel;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.YouTubeFeed;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<FeedSync>();
        serviceCollection.AddSingleton<IChannel, YouTubeFeedChannelImpl>();
    }
}
