#!/usr/bin/env python3
"""
ytstream-proxy — provides YouTube stream URLs for the Jellyfin Invidious channel.

Uses yt-dlp to resolve a direct HLS (m3u8) URL, then redirects Jellyfin's
ffmpeg to it. ffmpeg handles HLS natively — no piping or merging needed.

Endpoints:
  GET /stream/{videoId}          — redirect to best HLS stream up to 720p
  GET /stream/{videoId}?res=480  — request a specific max resolution
  GET /info/{videoId}            — Invidious video info JSON (debug)
"""

import http.server
import subprocess
import urllib.request
import json
import re
import logging
import os

INVIDIOUS = os.environ.get("INVIDIOUS_URL", "http://invidious.lan").rstrip("/")
YTDLP     = os.environ.get("YTDLP_PATH", "/usr/bin/yt-dlp")
PORT      = int(os.environ.get("PROXY_PORT", "3003"))

logging.basicConfig(level=logging.INFO, format="[%(asctime)s] %(levelname)s %(message)s")
log = logging.getLogger("ytstream-proxy")


def get_info(video_id):
    url = f"{INVIDIOUS}/api/v1/videos/{video_id}?fields=title,adaptiveFormats,formatStreams"
    req = urllib.request.Request(url, headers={"User-Agent": "ytstream-proxy/1.0"})
    with urllib.request.urlopen(req, timeout=15) as resp:
        return json.loads(resp.read())


def resolve_stream_url(video_id, max_res):
    """Run yt-dlp --get-url to get a direct HLS stream URL."""
    res = max_res or "720"
    fmt = (
        f"best[height<={res}][protocol=m3u8_native]"
        f"/best[height<={res}][protocol=m3u8]"
        f"/best[height<={res}]"
        f"/best"
    )
    result = subprocess.run(
        [YTDLP, "--quiet", "--no-warnings", "--no-playlist", "-f", fmt, "--get-url",
         f"https://www.youtube.com/watch?v={video_id}"],
        capture_output=True, text=True, timeout=30, cwd="/tmp",
    )
    urls = result.stdout.strip().splitlines()
    return urls[0] if urls else None, result.stderr


class StreamHandler(http.server.BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        log.info(fmt % args)

    def send_json(self, code, obj):
        body = json.dumps(obj, indent=2).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        path = self.path.split("?")[0].rstrip("/")
        params = {}
        if "?" in self.path:
            for kv in self.path.split("?", 1)[1].split("&"):
                if "=" in kv:
                    k, v = kv.split("=", 1)
                    params[k] = v

        # /info/{videoId}
        m = re.match(r"^/info/([A-Za-z0-9_-]{11})$", path)
        if m:
            try:
                self.send_json(200, get_info(m.group(1)))
            except Exception as e:
                self.send_json(500, {"error": str(e)})
            return

        # /stream/{videoId}
        m = re.match(r"^/stream/([A-Za-z0-9_-]{11})$", path)
        if not m:
            self.send_error(404, "Use /stream/{videoId} or /info/{videoId}")
            return

        video_id = m.group(1)
        max_res  = params.get("res", "720")

        log.info("Resolving stream: videoId=%s res=%s", video_id, max_res)

        try:
            stream_url, stderr = resolve_stream_url(video_id, max_res)
        except subprocess.TimeoutExpired:
            log.error("yt-dlp timed out for %s", video_id)
            self.send_error(504, "yt-dlp timed out")
            return
        except Exception as e:
            log.error("yt-dlp error for %s: %s", video_id, e)
            self.send_error(500, str(e))
            return

        if not stream_url:
            log.error("No URL for %s — stderr: %s", video_id, stderr[:300])
            self.send_error(500, "No stream URL available")
            return

        log.info("Redirect %s → %s...", video_id, stream_url[:70])
        self.send_response(302)
        self.send_header("Location", stream_url)
        self.send_header("Cache-Control", "no-cache")
        self.end_headers()


if __name__ == "__main__":
    server = http.server.ThreadingHTTPServer(("127.0.0.1", PORT), StreamHandler)
    log.info("ytstream-proxy listening on http://127.0.0.1:%d", PORT)
    log.info("yt-dlp: %s | Invidious: %s", YTDLP, INVIDIOUS)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        server.shutdown()
