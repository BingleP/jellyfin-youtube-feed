using Jellyfin.Plugin.InvidiousChannel.Api;
using InvidiousChannelImpl = Jellyfin.Plugin.InvidiousChannel.Channel.InvidiousChannel;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.InvidiousChannel;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<InvidiousApiClient>();
        serviceCollection.AddSingleton<IChannel, InvidiousChannelImpl>();
    }
}
