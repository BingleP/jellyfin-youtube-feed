# Jellyfin Invidious Channel

A Jellyfin channel plugin that lets you browse and watch YouTube through your local [Invidious](https://invidious.io) instance. Video playback is resolved via `yt-dlp` and streamed as HLS — no YouTube account or API key needed.

## How it works

```
Jellyfin UI
  → plugin fetches metadata from Invidious API (trending, popular)
  → click play → Jellyfin requests http://127.0.0.1:3003/stream/{videoId}
  → ytstream-proxy runs yt-dlp --get-url → HLS m3u8 URL
  → 302 redirect to manifest.googlevideo.com
  → Jellyfin's ffmpeg transcodes HLS stream to your browser
```

Invidious handles **metadata only** (titles, thumbnails, descriptions). Actual video is fetched directly from YouTube's CDN via `yt-dlp`.

Startup delay of ~5–10 seconds per video is normal — that's `yt-dlp` resolving the stream URL.

---

## Prerequisites

- **Jellyfin** 10.11.x installed as a system service (non-Docker), DLLs at `/usr/lib/jellyfin/`
- **.NET SDK 9.0** (`dotnet-sdk-9.0`)
- **yt-dlp** at `/usr/bin/yt-dlp` (keep it updated — YouTube changes formats frequently)
- **Python 3** (for the proxy, standard library only)
- A local **Invidious** instance (default assumed at `http://invidious.lan`)

---

## Installation

### 1. Install the stream proxy

```bash
cd proxy/
sudo ./install-proxy.sh
```

This copies `ytstream_proxy.py` to `/opt/ytstream-proxy/` and installs a systemd service that runs on `127.0.0.1:3003`.

Verify it's running:
```bash
systemctl status ytstream-proxy
curl http://127.0.0.1:3003/info/dQw4w9WgXcQ   # debug: fetch video info JSON
```

### 2. Build and install the Jellyfin plugin

```bash
sudo ./deploy.sh
sudo systemctl restart jellyfin
```

This builds the C# plugin in Release mode and installs it to `/var/lib/jellyfin/plugins/Invidious Channel_1.0.0.0/`.

### 3. Enable the channel in Jellyfin

1. Open Jellyfin → **Dashboard → Channels**
2. You should see **Invidious** listed — enable it
3. Go to **Home → Channels → Invidious** to browse Trending / Popular

---

## Configuration

In Jellyfin: **Dashboard → Plugins → Invidious Channel → Settings**

| Setting | Default | Description |
|---------|---------|-------------|
| Invidious URL | `http://invidious.lan` | Base URL of your Invidious instance |
| API Token | _(empty)_ | Optional session cookie if your instance requires login |

---

## Redeploy after changes

```bash
# Plugin changes
cd jellyfin-invidious-channel/
sudo ./deploy.sh
sudo systemctl restart jellyfin

# Proxy changes
sudo cp proxy/ytstream_proxy.py /opt/ytstream-proxy/
sudo systemctl restart ytstream-proxy
```

If videos still show the old behaviour after a plugin update, clear Jellyfin's channel cache:
```bash
sudo rm -rf /var/lib/jellyfin/metadata/channels/
sudo systemctl restart jellyfin
```

---

## Proxy configuration

Edit `/opt/ytstream-proxy/ytstream_proxy.py` to change:

```python
INVIDIOUS = "http://invidious.lan"   # your Invidious base URL
YTDLP     = "/usr/bin/yt-dlp"       # path to yt-dlp binary
PORT      = 3003                     # proxy listen port
```

After editing, restart the service:
```bash
sudo systemctl restart ytstream-proxy
```

To request a different max resolution, append `?res=480` (or 360, 1080, etc.) to the stream URL — configurable in the plugin source (`InvidiousApiClient.cs → GetStreamUrl`).

---

## Repository layout

```
jellyfin-invidious-channel/
├── Api/
│   ├── InvidiousApiClient.cs   # Invidious HTTP client
│   └── Models.cs               # JSON data models
├── Channel/
│   └── InvidiousChannel.cs     # IChannel implementation (Trending, Popular)
├── Plugin.cs                   # Plugin entry point
├── PluginConfiguration.cs      # Settings (InvidiousUrl, ApiToken)
├── ServiceRegistrator.cs       # DI registration (required — Jellyfin won't auto-discover)
├── Jellyfin.Plugin.InvidiousChannel.csproj
├── deploy.sh                   # Build + install plugin
└── proxy/
    ├── ytstream_proxy.py        # Python HLS redirect proxy
    ├── ytstream-proxy.service   # systemd unit template
    └── install-proxy.sh         # Proxy install script
```

---

## Troubleshooting

**Channel shows 0 items**
- Check Jellyfin logs: `journalctl -u jellyfin -f`
- Confirm Invidious is reachable: `curl http://invidious.lan/api/v1/trending`
- Bump `DataVersion` in `InvidiousChannel.cs` and redeploy to bust the cache

**Video plays for a second then stops / 403 errors**
- Update yt-dlp: `sudo yt-dlp -U`
- YouTube tokens expire; fresh yt-dlp cookies may be needed

**`No stream URL available` in proxy logs**
- Run manually to see yt-dlp output:
  ```bash
  yt-dlp -f "best[protocol=m3u8_native]/best" --get-url "https://www.youtube.com/watch?v=VIDEO_ID"
  ```

**`FormatException: Unrecognized Guid format`** in Jellyfin logs
- This means `MediaSourceInfo.Id` was set to a raw YouTube video ID. The plugin uses a deterministic MD5-derived GUID to avoid this — if you see it, ensure you're running the latest build.
