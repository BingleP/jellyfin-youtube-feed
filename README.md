# Jellyfin Invidious Channel

A Jellyfin channel plugin that lets you browse and watch YouTube through your self-hosted [Invidious](https://invidious.io) instance. Video playback is resolved via `yt-dlp` and streamed as HLS — no YouTube account or API key required.

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

> **Note:** Expect a 5–10 second startup delay per video — that's `yt-dlp` resolving the stream URL.

---

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| **Jellyfin 10.11.x** | System service install (non-Docker), DLLs at `/usr/lib/jellyfin/` |
| **.NET SDK 9.0** | `dotnet-sdk-9.0` |
| **yt-dlp** | Keep updated — YouTube changes formats frequently |
| **Python 3** | Standard library only, no pip packages needed |
| **Invidious** | A running self-hosted instance |

---

## Installation

### 1. Clone the repo

```bash
git clone https://github.com/BingleP/jellyfin-invidious-channel.git
cd jellyfin-invidious-channel
```

### 2. Install the stream proxy

```bash
cd proxy/
sudo ./install-proxy.sh
```

This copies `ytstream_proxy.py` to `/opt/ytstream-proxy/` and installs a systemd service listening on `127.0.0.1:3003`.

Verify it's running:
```bash
systemctl status ytstream-proxy
curl http://127.0.0.1:3003/info/dQw4w9WgXcQ   # should return video info JSON
```

### 3. Build and install the Jellyfin plugin

```bash
cd ..
sudo ./deploy.sh
sudo systemctl restart jellyfin
```

This builds the C# plugin in Release mode and installs it to `/var/lib/jellyfin/plugins/Invidious Channel_1.0.0.0/`.

### 4. Enable the channel in Jellyfin

1. Open Jellyfin → **Dashboard → Channels**
2. You should see **Invidious** listed — enable it
3. Go to **Home → Channels → Invidious** to browse Trending / Popular

---

## Configuration

### Plugin settings

In Jellyfin: **Dashboard → Plugins → Invidious Channel → Settings**

| Setting | Default | Description |
|---------|---------|-------------|
| Invidious URL | `http://invidious.lan` | Base URL of your Invidious instance |
| API Token | _(empty)_ | Optional session cookie if your instance requires auth |

### Proxy settings

Edit the environment variables in `/etc/systemd/system/ytstream-proxy.service`:

```ini
Environment=INVIDIOUS_URL=http://your-invidious-host   # your Invidious base URL
Environment=YTDLP_PATH=/usr/bin/yt-dlp                 # path to yt-dlp (optional)
Environment=PROXY_PORT=3003                             # listen port (optional)
```

After editing:
```bash
sudo systemctl daemon-reload
sudo systemctl restart ytstream-proxy
```

---

## Updating

```bash
git pull

# Redeploy plugin
sudo ./deploy.sh
sudo systemctl restart jellyfin

# Redeploy proxy
sudo cp proxy/ytstream_proxy.py /opt/ytstream-proxy/
sudo systemctl restart ytstream-proxy
```

If videos show stale behaviour after a plugin update, clear Jellyfin's channel cache:
```bash
sudo rm -rf /var/lib/jellyfin/metadata/channels/
sudo systemctl restart jellyfin
```

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
- Confirm Invidious is reachable: `curl http://YOUR_INVIDIOUS_HOST/api/v1/trending`
- Bump `DataVersion` in `InvidiousChannel.cs` and redeploy to bust the cache

**Video plays for a second then stops / 403 errors**
- Update yt-dlp: `sudo yt-dlp -U`
- YouTube CDN tokens expire quickly; a fresh yt-dlp version usually fixes it

**`No stream URL available` in proxy logs**
- Test yt-dlp directly:
  ```bash
  yt-dlp -f "best[protocol=m3u8_native]/best" --get-url "https://www.youtube.com/watch?v=VIDEO_ID"
  ```

**`FormatException: Unrecognized Guid format`** in Jellyfin logs
- Ensure you're running the latest build. The plugin derives a valid GUID from each video ID via MD5 to avoid this.

---

## Limitations

- Jellyfin 10.11.x on Linux with a system (non-Docker) install only — DLLs are referenced directly from `/usr/lib/jellyfin/`
- ~5–10s per-video startup delay while yt-dlp resolves the stream URL
- No search support (Invidious search API works but is not wired up to a Jellyfin search interface)
- YouTube may occasionally return no HLS URL for a video; updating yt-dlp usually resolves it
