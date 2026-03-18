# jellyfin-youtube-feed

A Jellyfin plugin that populates a library with your personalised YouTube recommended feed. Videos are fetched via `yt-dlp` using your browser cookies and played directly from YouTube's CDN — no YouTube API key, no Invidious instance, no extra ffmpeg processes.

## How it works

```
On startup / every N hours:
  → FeedSync runs yt-dlp with your cookies.txt
  → fetches https://www.youtube.com/feed/recommended
  → incrementally writes .strm files to your configured folder
    (adds new videos, removes videos no longer in feed, leaves unchanged ones untouched)
  → Jellyfin's Movies library picks up the .strm files automatically

On playback:
  → Jellyfin reads the .strm file → sees http://127.0.0.1:3003/stream/{videoId}
  → ytstream-proxy checks its in-memory URL cache (4-hour TTL)
      cache hit:  returns cached CDN URL instantly
      cache miss: runs yt-dlp --get-url to resolve CDN URL (5–10s)
  → proxy issues HTTP 302 → YouTube CDN MP4 URL
  → Jellyfin's ffmpeg follows the redirect and streams directly from YouTube CDN
```

> **Note:** The first play of any video incurs a 5–10 second startup delay while `yt-dlp` resolves the CDN URL. Subsequent plays of the same video within 4 hours are instant.

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

You need to export your YouTube cookies in Netscape format using a browser extension.

**Firefox** — install [Get cookies.txt LOCALLY](https://addons.mozilla.org/firefox/addon/get-cookies-txt-locally/) from the Firefox Add-ons store.

**Chrome** — install [Get cookies.txt LOCALLY](https://chromewebstore.google.com/detail/get-cookiestxt-locally/cclelndahbckbenkjhflpdbgdldlbecc) from the Chrome Web Store.

Then:
1. Make sure you are **logged into YouTube** in your browser.
2. Click the extension icon in the toolbar while on **youtube.com**.
3. Select **"Export" → "Current Site"** to download `youtube.com_cookies.txt`.
4. Copy the file to your Jellyfin server somewhere the `jellyfin` user can read:
   ```bash
   scp ~/Downloads/youtube.com_cookies.txt yourserver:/var/lib/jellyfin/plugins/youtube-cookies.txt
   sudo chown jellyfin:jellyfin /var/lib/jellyfin/plugins/youtube-cookies.txt
   ```

> **Note:** Your home directory is typically not accessible to the `jellyfin` user. Keep the cookies file somewhere under `/var/lib/jellyfin/` or another path the `jellyfin` user can read.

> **Note:** Cookies expire periodically. If the feed stops updating, re-export and replace the file.

### 3. Create a folder for feed files

Create the folder where `.strm` files will be written and give Jellyfin ownership:

```bash
sudo mkdir -p /mnt/yourDisk/YouTube
sudo chown jellyfin:jellyfin /mnt/yourDisk/YouTube
```

### 4. Deploy the plugin and proxy

```bash
sudo ./deploy.sh
sudo systemctl restart jellyfin
```

This builds the plugin DLL, copies `ytstream_proxy.py` into `/var/lib/jellyfin/plugins/YouTube Feed_1.0.0.0/`, and installs and starts the `ytstream-proxy` systemd service. Everything lives in the plugin folder — no separate `/opt/` directory.

### 5. Configure the plugin in Jellyfin

1. Go to **Dashboard → Plugins → YouTube Feed → Settings**
2. Fill in the settings:

| Setting | Description |
|---------|-------------|
| **.strm Folder Path** | Full path to the folder you created in step 3, e.g. `/mnt/yourDisk/YouTube` |
| **Cookies File Path** | Full path to your `cookies.txt` file, e.g. `/var/lib/jellyfin/plugins/youtube-cookies.txt` |
| **yt-dlp Path** | Path to the yt-dlp binary (default: `/usr/bin/yt-dlp`) |
| **Feed Refresh Interval (hours)** | How often to re-fetch the feed (default: `6`) |

3. Click **Save**. The plugin will immediately begin fetching your feed and writing `.strm` files.

### 6. Add a Movies library pointing at the feed folder

> **Important:** The library type must be **Movies**. Jellyfin only processes `.strm` files in Movies libraries.

1. Go to **Dashboard → Libraries → Add Media Library**
2. Set **Content type** to **Movies**
3. Set the folder to the same path you configured in step 5 (e.g. `/mnt/yourDisk/YouTube`)
4. Under **Advanced**, uncheck all metadata fetchers and image fetchers — these will just slow down scans trying to look up YouTube videos as movies
5. Click **Save**

Your feed videos will appear in the library within a minute or two. Jellyfin watches the folder and picks up new `.strm` files automatically.

---

## How the feed is populated

Each sync cycle the plugin:

1. Runs `yt-dlp --cookies /your/cookies.txt --flat-playlist --print "%(id)s\t%(title)s" https://www.youtube.com/feed/recommended`
2. Incrementally updates `.strm` files — only writes files that are new or changed, only deletes files no longer in the feed. Unchanged videos are left on disk untouched to avoid Jellyfin metadata churn.

Each `.strm` file contains a single line like:
```
http://127.0.0.1:3003/stream/dQw4w9WgXcQ
```

When Jellyfin plays the file, it hits the proxy, which resolves the real CDN URL and redirects.

You can also manually drop `.strm` files into the folder to pin specific videos.

---

## Stream proxy configuration

The proxy reads configuration from `/var/lib/jellyfin/plugins/YouTube Feed_1.0.0.0/config.env` (written by `install-proxy.sh`):

| Variable | Default | Description |
|----------|---------|-------------|
| `YTDLP_PATH` | `/usr/bin/yt-dlp` | Path to yt-dlp binary |
| `PROXY_PORT` | `3003` | Port to listen on |
| `COOKIES_FILE` | _(empty)_ | Path to cookies.txt for authenticated resolution |
| `URL_CACHE_TTL` | `14400` | Seconds to cache resolved CDN URLs (default: 4 hours) |
| `URL_CACHE_MAX` | `500` | Maximum cached entries before LRU eviction |

---

## Updating

```bash
git pull
sudo ./deploy.sh
sudo systemctl restart jellyfin
```

---

## Repository layout

```
jellyfin-youtube-feed/
├── Api/
│   ├── FeedSync.cs                 # Runs yt-dlp, incrementally writes .strm files
│   ├── FeedSyncService.cs          # IHostedService — runs FeedSync on startup and on interval
│   └── UninstallController.cs      # POST /youtubefeed/cleanup — removes proxy service and files
├── Configuration/
│   └── configurationpage.html      # Plugin settings UI (includes Uninstall button)
├── Plugin.cs                       # Plugin entry point
├── PluginConfiguration.cs          # Settings model
├── ServiceRegistrator.cs           # DI registration
├── Jellyfin.Plugin.YouTubeFeed.csproj
├── deploy.sh                       # Build + install plugin + proxy in one command
└── proxy/
    ├── ytstream_proxy.py           # HTTP proxy: resolves video ID → 302 to CDN URL
    ├── ytstream-proxy.service      # systemd unit
    └── install-proxy.sh            # Proxy install script
```

---

## Uninstalling

The plugin configuration page has an **Uninstall Plugin** button in the **Danger Zone** section. Clicking it will:

1. Stop and disable the `ytstream-proxy` systemd service
2. Delete `/opt/ytstream-proxy/`
3. Delete the `.strm` feed folder
4. Delete the plugin configuration XML
5. Uninstall the plugin from Jellyfin
6. Restart Jellyfin to complete removal

Everything the plugin added is removed in a single click. No manual cleanup needed.

You will still need to manually remove the Jellyfin library that pointed at the feed folder (**Dashboard → Libraries**).

---

## Troubleshooting

**No videos appear in the library**
- Check that **Cookies File Path** and **.strm Folder Path** are set in plugin settings
- Check Jellyfin logs: `journalctl -u jellyfin -f`
- Look for `FeedSync:` log lines — they show exactly what happened
- Confirm the library type is **Movies** (not Videos/Home Videos — those ignore `.strm` files)

**Feed sync returns no videos / cookies may be expired**
- Re-export your cookies from the browser and replace the file
- Test yt-dlp directly:
  ```bash
  yt-dlp --cookies /your/cookies.txt --flat-playlist --print "%(id)s\t%(title)s" https://www.youtube.com/feed/recommended
  ```

**Video plays for a second then stops / 403 errors**
- Update yt-dlp: `sudo yt-dlp -U`
- YouTube CDN tokens expire; a fresh yt-dlp version usually fixes it

**Black screen or no video**
- Check proxy logs: `journalctl -u ytstream-proxy -f`
- Confirm the proxy is running: `systemctl status ytstream-proxy`
- Test resolution directly:
  ```bash
  curl -v http://127.0.0.1:3003/stream/dQw4w9WgXcQ
  # Should return HTTP 302 with a Location header pointing to a YouTube CDN URL
  ```

**Proxy not running / connection refused on port 3003**
- Run `sudo ./deploy.sh` again — it reinstalls and restarts the proxy
- Or start it manually: `sudo systemctl start ytstream-proxy`

---

## Limitations

- Jellyfin 10.11.x on Linux with a system (non-Docker) install only
- First play of any video has a 5–10s startup delay while yt-dlp resolves the CDN URL (subsequent plays within 4 hours are instant)
- YouTube cookies expire periodically and need re-exporting
- Recommended feed content is whatever YouTube's algorithm serves for your account
- Combined streams (video+audio in one file) are preferred for simplicity; these top out at 720p. Separate DASH streams would require a more complex proxy.
