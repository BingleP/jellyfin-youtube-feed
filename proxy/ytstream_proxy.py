#!/usr/bin/env python3
"""
ytstream-proxy — resolves YouTube video IDs to CDN URLs and issues HTTP 302.

Jellyfin hits /stream/{videoId}, yt-dlp resolves a direct MP4 CDN URL,
and we redirect. Jellyfin's ffmpeg follows the redirect and reads the MP4
directly — no intermediate ffmpeg process, no piping.
"""

import http.server
import subprocess
import re
import logging
import os
import threading
import time
from collections import OrderedDict

YTDLP         = os.environ.get("YTDLP_PATH", "/usr/bin/yt-dlp")
PORT          = int(os.environ.get("PROXY_PORT", "3003"))
COOKIES_FILE  = os.environ.get("COOKIES_FILE", "")
URL_CACHE_TTL = int(os.environ.get("URL_CACHE_TTL", "14400"))
URL_CACHE_MAX = int(os.environ.get("URL_CACHE_MAX", "500"))

logging.basicConfig(level=logging.INFO, format="[%(asctime)s] %(levelname)s %(message)s")
log = logging.getLogger("ytstream-proxy")

_cache: OrderedDict = OrderedDict()
_cache_lock = threading.Lock()
_in_flight: dict = {}
_in_flight_lock = threading.Lock()


def _get_cached(video_id: str):
    if URL_CACHE_TTL <= 0:
        return None
    with _cache_lock:
        entry = _cache.get(video_id)
        if entry and (time.monotonic() - entry["ts"]) < URL_CACHE_TTL:
            _cache.move_to_end(video_id)
            return entry["url"]
        if entry:
            del _cache[video_id]
    return None


def _set_cached(video_id: str, url: str):
    with _cache_lock:
        _cache[video_id] = {"url": url, "ts": time.monotonic()}
        _cache.move_to_end(video_id)
        while len(_cache) > URL_CACHE_MAX:
            _cache.popitem(last=False)


def _resolve(video_id: str) -> str:
    # Combined-stream selector: single URL, H.264+AAC MP4, up to 720p.
    # Combined streams avoid DASH complexity and work with a plain 302 redirect.
    fmt = "best[height<=720][vcodec^=avc1]/best[height<=720]/best"
    cmd = [
        YTDLP, "--quiet", "--no-warnings", "--no-playlist",
        "--socket-timeout", "10",
        "--get-url", "-f", fmt,
        f"https://www.youtube.com/watch?v={video_id}",
    ]
    if COOKIES_FILE:
        cmd += ["--cookies", COOKIES_FILE]
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=30, cwd="/tmp")
    urls = [l for l in result.stdout.strip().splitlines() if l.startswith("http")]
    if not urls:
        raise RuntimeError(result.stderr[:300] or "yt-dlp returned no URL")
    return urls[0]


def resolve(video_id: str) -> str:
    cached = _get_cached(video_id)
    if cached:
        log.info("Cache hit for %s", video_id)
        return cached

    with _in_flight_lock:
        if video_id in _in_flight:
            event, is_resolver = _in_flight[video_id], False
        else:
            event = threading.Event()
            _in_flight[video_id] = event
            is_resolver = True

    if not is_resolver:
        event.wait(timeout=35)
        return _get_cached(video_id) or _resolve(video_id)

    try:
        url = _resolve(video_id)
        _set_cached(video_id, url)
        return url
    finally:
        with _in_flight_lock:
            _in_flight.pop(video_id, None)
        event.set()


class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        log.info(fmt % args)

    def do_GET(self):
        m = re.match(r"^/stream/([A-Za-z0-9_-]{11})$", self.path.split("?")[0])
        if not m:
            self.send_error(404, "Use /stream/{videoId}")
            return
        video_id = m.group(1)
        log.info("Resolving %s", video_id)
        try:
            url = resolve(video_id)
        except subprocess.TimeoutExpired:
            self.send_error(504, "yt-dlp timed out")
            return
        except Exception as e:
            log.error("Failed to resolve %s: %s", video_id, e)
            self.send_error(500, str(e))
            return
        self.send_response(302)
        self.send_header("Location", url)
        self.end_headers()
        log.info("302 → CDN for %s", video_id)


if __name__ == "__main__":
    server = http.server.ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
    log.info("ytstream-proxy listening on http://127.0.0.1:%d | yt-dlp: %s | cache TTL: %ds",
             PORT, YTDLP, URL_CACHE_TTL)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        server.shutdown()
