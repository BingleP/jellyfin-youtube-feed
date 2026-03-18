# jellyfin-youtube-feed

A Jellyfin channel plugin that populates your channel with your personalised YouTube recommended feed. Videos are fetched via `yt-dlp` using your browser cookies and streamed as MPEG-TS — no YouTube API key or Invidious instance required.

## How it works

```
On channel open:
  → FeedSync runs yt-dlp with your cookies.txt
  → fetches https://www.youtube.com/feed/recommended
  → incrementally updates .strm files in the plugin's strm/ directory
    (only adds new videos, only removes videos no longer in feed)
  → channel scans strm/ and displays videos with thumbnails

On playback:
  → Jellyfin requests http://127.0.0.1:3003/stream/{videoId}
  → ytstream-proxy checks its in-memory URL cache (4-hour TTL)
    → cache hit:  immediately spawns ffmpeg with cached CDN URLs (~0ms)
    → cache miss: runs yt-dlp --get-url to resolve CDN URLs (5–10s)
  → ffmpeg stream-copies H.264 video + AAC audio into MPEG-TS → stdout pipe
  → Jellyfin receives MPEG-TS and direct-streams to compatible clients
    (no Jellyfin re-encode for H.264-capable clients)
```

> **Note:** The first play of any video incurs a 5–10 second startup delay while `yt-dlp` resolves the CDN URL. Subsequent plays of the same video within 4 hours are instant (served from cache).

---

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| **Jellyfin 10.11.x** | System service install (non-Docker), DLLs at `/usr/lib/jellyfin/` |
| **.NET SDK 9.0** | `dotnet-sdk-9.0` |
| **yt-dlp** | Keep updated — YouTube changes formats frequently |
| **Python 3** | Standard library only, no pip packages needed |
| **YouTube cookies.txt** | Exported from your browser while logged into YouTube |

---

## Installation

### 1. Clone the repo

```bash
git clone https://github.com/BingleP/jellyfin-youtube-feed.git
cd jellyfin-youtube-feed
```

### 2. Export your YouTube cookies

You need to export your YouTube cookies in Netscape format using a browser extension. Steps are the same for both browsers — only the install link differs.

**Firefox** — install [Get cookies.txt LOCALLY](https://addons.mozilla.org/firefox/addon/get-cookies-txt-locally/) from the Firefox Add-ons store.

**Chrome** — install [Get cookies.txt LOCALLY](https://chromewebstore.google.com/detail/get-cookiestxt-locally/cclelndahbckbenkjhflpdbgdldlbecc) from the Chrome Web Store.

Then:
1. Make sure you are **logged into YouTube** in your browser.
2. Click the extension icon in the toolbar while on **youtube.com**.
3. Select **"Export" → "Current Site"** to download `youtube.com_cookies.txt`.
4. Copy the file to your Jellyfin server:
   ```bash
   scp ~/Downloads/youtube.com_cookies.txt yourserver:/home/youruser/youtube-cookies.txt
   ```

> **Note:** Cookies expire periodically. If the feed stops updating, re-export and replace the file.

### 3. Install the stream proxy

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

### 4. Build and install the plugin

```bash
cd ..
sudo ./deploy.sh
sudo systemctl restart jellyfin
```

This builds the plugin and installs it to `/var/lib/jellyfin/plugins/YouTube Feed_1.0.0.0/`.

### 5. Configure the plugin

In Jellyfin: **Dashboard → Plugins → YouTube Feed → Settings**

| Setting | Default | Description |
|---------|---------|-------------|
| **Cookies File Path** | _(empty)_ | Full path to your `cookies.txt` file |
| **yt-dlp Path** | `/usr/bin/yt-dlp` | Path to the yt-dlp binary |
| **Feed Refresh Interval Hours** | `6` | How often to re-fetch your recommended feed |

### 6. Open the channel

1. Go to **Home → Channels → YouTube Feed**
2. On first open, the plugin fetches your recommended feed (takes a few seconds)
3. Videos appear with titles and thumbnails — click to play

---

## How the feed is populated

Each time you open the channel (and the refresh interval has elapsed), the plugin:

1. Runs `yt-dlp --cookies /your/cookies.txt --flat-playlist --print "%(id)s\t%(title)s" https://www.youtube.com/feed/recommended`
2. Incrementally updates `.strm` files — only writes files that are new or changed, only deletes files no longer in the feed. Unchanged videos are left on disk untouched to avoid Jellyfin metadata churn.
   ```
   /var/lib/jellyfin/plugins/YouTube Feed_1.0.0.0/strm/
       Some Video Title.strm        ← contains: https://www.youtube.com/watch?v=abc123
       Another Video.strm
       ...
   ```

You can also manually drop `.strm` files into that directory to pin specific videos.

---

## Stream proxy configuration

The proxy reads configuration from environment variables set in the systemd service file (`proxy/ytstream-proxy.service`):

| Variable | Default | Description |
|----------|---------|-------------|
| `YTDLP_PATH` | `/usr/bin/yt-dlp` | Path to yt-dlp binary |
| `FFMPEG_PATH` | `/usr/lib/jellyfin-ffmpeg/ffmpeg` | Path to ffmpeg binary |
| `PROXY_PORT` | `3003` | Port to listen on |
| `COOKIES_FILE` | _(empty)_ | Path to cookies.txt for authenticated resolution |
| `URL_CACHE_TTL` | `14400` | Seconds to cache resolved CDN URLs (default: 4 hours) |
| `URL_CACHE_MAX` | `500` | Maximum cached entries before LRU eviction |

---

## Updating

```bash
git pull

# Redeploy plugin
sudo ./deploy.sh
sudo systemctl restart jellyfin

# Redeploy proxy
sudo cp proxy/ytstream_proxy.py /opt/ytstream-proxy/
sudo systemctl daemon-reload
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
jellyfin-youtube-feed/
├── Api/
│   ├── FeedSync.cs                     # Runs yt-dlp, incrementally writes .strm files
│   └── UninstallController.cs          # POST /youtubefeed/cleanup — removes proxy service and files
├── Channel/
│   └── YouTubeFeedChannel.cs           # IChannel implementation
├── Configuration/
│   └── configurationpage.html          # Plugin settings UI (includes Uninstall button)
├── Plugin.cs                           # Plugin entry point
├── PluginConfiguration.cs              # Settings (CookiesFilePath, YtDlpPath, FeedRefreshIntervalHours)
├── ServiceRegistrator.cs               # DI registration
├── Jellyfin.Plugin.YouTubeFeed.csproj
├── deploy.sh                           # Build + install plugin
└── proxy/
    ├── ytstream_proxy.py               # Stream proxy with URL caching and ffmpeg pipeline
    ├── ytstream-proxy.service          # systemd unit (includes resource limits)
    └── install-proxy.sh               # Proxy install script
```

---

## Uninstalling

The plugin configuration page has an **Uninstall Plugin** button (in the Danger Zone section at the bottom). Clicking it will:

1. Stop and remove the `ytstream-proxy` systemd service
2. Delete `/opt/ytstream-proxy/`
3. Uninstall the plugin from Jellyfin
4. Restart Jellyfin to complete removal

This removes everything the plugin added to your system in a single click. No manual cleanup needed.

---

## Troubleshooting

**Channel shows 0 items**
- Check that `CookiesFilePath` is set correctly in plugin settings
- Check Jellyfin logs: `journalctl -u jellyfin -f`
- Look for `FeedSync:` log lines — they'll tell you what went wrong

**Feed sync returns no videos / cookies may be expired**
- Re-export your cookies from the browser and replace the file
- Test yt-dlp directly:
  ```bash
  yt-dlp --cookies /your/cookies.txt --flat-playlist --print "%(id)s\t%(title)s" https://www.youtube.com/feed/recommended
  ```

**Video plays for a second then stops / 403 errors**
- Update yt-dlp: `sudo yt-dlp -U`
- YouTube CDN tokens expire; a fresh yt-dlp version usually fixes it

**`No stream URL available` in proxy logs**
- Test yt-dlp directly:
  ```bash
  yt-dlp -f "bestvideo[vcodec^=avc1]+bestaudio/best" --get-url "https://www.youtube.com/watch?v=VIDEO_ID"
  ```

**Check proxy logs**
```bash
journalctl -u ytstream-proxy -f
```

---

## Limitations

- Jellyfin 10.11.x on Linux with a system (non-Docker) install only
- First play of any video has a 5–10s startup delay while yt-dlp resolves the CDN URL (subsequent plays within 4 hours are instant)
- YouTube cookies expire periodically and need re-exporting
- Recommended feed content is whatever YouTube's algorithm serves for your account
