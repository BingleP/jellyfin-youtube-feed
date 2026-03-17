# jellyfin-youtube-feed

A Jellyfin channel plugin that populates your channel with your personalised YouTube recommended feed. Videos are fetched via `yt-dlp` using your browser cookies and streamed as HLS — no YouTube API key or Invidious instance required.

## How it works

```
On channel open:
  → FeedSync runs yt-dlp with your cookies.txt
  → fetches https://www.youtube.com/feed/recommended
  → writes a .strm file per video into the plugin's strm/ directory
  → channel scans strm/ and displays videos with thumbnails

On playback:
  → Jellyfin requests http://127.0.0.1:3003/stream/{videoId}
  → ytstream-proxy runs yt-dlp --get-url → HLS m3u8 URL
  → 302 redirect to manifest.googlevideo.com
  → Jellyfin's ffmpeg transcodes HLS stream to your client
```

> **Note:** Expect a 5–10 second startup delay per video — that's `yt-dlp` resolving the stream URL. Feed sync also takes a few seconds on first open.

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
2. Clears old `.strm` files from the plugin's data directory
3. Writes a fresh `.strm` file for each recommended video:
   ```
   /var/lib/jellyfin/plugins/YouTube Feed_1.0.0.0/strm/
       Some Video Title.strm        ← contains: https://www.youtube.com/watch?v=abc123
       Another Video.strm
       ...
   ```

You can also manually drop `.strm` files into that directory to pin specific videos.

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
jellyfin-youtube-feed/
├── Api/
│   └── FeedSync.cs                     # Runs yt-dlp, writes .strm files
├── Channel/
│   └── YouTubeFeedChannel.cs           # IChannel implementation
├── Plugin.cs                           # Plugin entry point
├── PluginConfiguration.cs              # Settings (CookiesFilePath, YtDlpPath, FeedRefreshIntervalHours)
├── ServiceRegistrator.cs               # DI registration
├── Jellyfin.Plugin.YouTubeFeed.csproj
├── deploy.sh                           # Build + install plugin
└── proxy/
    ├── ytstream_proxy.py               # Python HLS redirect proxy
    ├── ytstream-proxy.service          # systemd unit template
    └── install-proxy.sh               # Proxy install script
```

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
- YouTube CDN tokens expire quickly; a fresh yt-dlp version usually fixes it

**`No stream URL available` in proxy logs**
- Test yt-dlp directly:
  ```bash
  yt-dlp -f "best[protocol=m3u8_native]/best" --get-url "https://www.youtube.com/watch?v=VIDEO_ID"
  ```

---

## Limitations

- Jellyfin 10.11.x on Linux with a system (non-Docker) install only
- ~5–10s per-video startup delay while yt-dlp resolves the stream URL
- YouTube cookies expire periodically and need re-exporting
- Recommended feed content is whatever YouTube's algorithm serves for your account
