#!/usr/bin/env python3
"""
ytstream-proxy — pipes YouTube streams as MPEG-TS for Jellyfin channel playback.

Jellyfin hardcodes -f mpegts for all channel streams, so we can't redirect to
a raw mp4/videoplayback URL. Instead, we resolve the stream URL with yt-dlp and
pipe it through ffmpeg → MPEG-TS directly to Jellyfin's ffmpeg via HTTP.

Endpoints:
  GET /stream/{videoId}          — stream as MPEG-TS up to 720p
  GET /stream/{videoId}?res=480  — request a specific max resolution
  GET /info/{videoId}            — debug: show yt-dlp JSON info
"""

import http.server
import subprocess
import shutil
import json
import re
import logging
import os
import threading
import time
from collections import OrderedDict

YTDLP         = os.environ.get("YTDLP_PATH", "/usr/bin/yt-dlp")
FFMPEG        = os.environ.get("FFMPEG_PATH", "/usr/lib/jellyfin-ffmpeg/ffmpeg")
PORT          = int(os.environ.get("PROXY_PORT", "3003"))
COOKIES_FILE  = os.environ.get("COOKIES_FILE", "")
# Resolved CDN URLs are cached this long (seconds). YouTube tokens last ~6 hours;
# 4 hours gives a safe margin. Set to 0 to disable caching.
URL_CACHE_TTL  = int(os.environ.get("URL_CACHE_TTL", "14400"))
URL_CACHE_MAX  = int(os.environ.get("URL_CACHE_MAX", "500"))   # max entries (LRU eviction)

logging.basicConfig(level=logging.INFO, format="[%(asctime)s] %(levelname)s %(message)s")
log = logging.getLogger("ytstream-proxy")


# ---------------------------------------------------------------------------
# URL cache — avoids spawning yt-dlp for every play of a recently-seen video.
# Ordered so we can evict the least-recently-used entry when full.
# Maps cache-key → {"urls": [...], "audio_copy": bool, "ts": float}
# ---------------------------------------------------------------------------
_url_cache: OrderedDict = OrderedDict()
_url_cache_lock = threading.Lock()

# In-flight deduplication — if two requests arrive for the same video
# simultaneously, only one yt-dlp process runs; the other waits for it.
_in_flight: dict = {}          # key → threading.Event
_in_flight_lock = threading.Lock()


def _cache_key(video_id: str, res: str) -> str:
    return f"{video_id}:{res}"


def _get_cached(video_id: str, res: str):
    """Return cached entry dict, or None if missing/expired."""
    if URL_CACHE_TTL <= 0:
        return None
    key = _cache_key(video_id, res)
    with _url_cache_lock:
        entry = _url_cache.get(key)
        if entry and (time.monotonic() - entry["ts"]) < URL_CACHE_TTL:
            # Move to end (most-recently-used)
            _url_cache.move_to_end(key)
            return entry
        elif entry:
            # Expired — evict now
            del _url_cache[key]
    return None


def _set_cached(video_id: str, res: str, urls: list, audio_copy: bool):
    key = _cache_key(video_id, res)
    with _url_cache_lock:
        if key in _url_cache:
            _url_cache.move_to_end(key)
        _url_cache[key] = {"urls": urls, "audio_copy": audio_copy, "ts": time.monotonic()}
        # Evict oldest entry if over the size limit
        while len(_url_cache) > URL_CACHE_MAX:
            _url_cache.popitem(last=False)


def _do_resolve(video_id: str, res: str):
    """
    Call yt-dlp to get CDN stream URLs.
    Returns (urls: list[str], audio_copy: bool, stderr: str).
    audio_copy=True when the selected audio stream is AAC (can be copied into MPEG-TS).
    """
    # Format selector priority:
    #  1. H.264 video + AAC audio (DASH) — both can be stream-copied, zero transcoding
    #  2. H.264 video + any audio (DASH) — video copy, audio re-encode
    #  3. H.264 combined stream          — may need audio re-encode
    #  4. Any combined stream up to res  — fallback
    #  5. Absolute fallback
    fmt = (
        f"bestvideo[height<={res}][vcodec^=avc1][acodec=none]"
        f"+bestaudio[acodec^=mp4a]"
        f"/bestvideo[height<={res}][vcodec^=avc1]+bestaudio"
        f"/best[height<={res}][vcodec^=avc1]"
        f"/best[height<={res}]"
        f"/best"
    )
    cmd = [
        YTDLP,
        "--quiet", "--no-warnings", "--no-playlist",
        "--socket-timeout", "10",           # don't hang on slow connections
        "--extractor-args", "youtube:player_client=android,web",
    ]
    if COOKIES_FILE:
        cmd += ["--cookies", COOKIES_FILE]
    # --print "%(acodec)s" followed by --get-url gives us: codec line(s) then url line(s).
    # We use this to detect whether audio can be stream-copied.
    cmd += [
        "--print", "%(acodec)s",
        "--get-url",
        f"https://www.youtube.com/watch?v={video_id}",
    ]

    result = subprocess.run(cmd, capture_output=True, text=True, timeout=30, cwd="/tmp")
    lines = result.stdout.strip().splitlines()

    # yt-dlp emits --print lines before --get-url lines, one per stream selected.
    # For DASH (2 streams): acodec_video\nacodec_audio\nvideo_url\naudio_url
    # For combined (1 stream): acodec\nurl
    # We split at the first http(s):// line.
    codec_lines = []
    url_lines = []
    for line in lines:
        if line.startswith("http://") or line.startswith("https://"):
            url_lines.append(line)
        else:
            codec_lines.append(line)

    # Determine whether audio codec is AAC (mp4a) — if so, we can copy it.
    audio_copy = False
    if codec_lines:
        # Last codec line corresponds to the audio stream
        audio_codec = codec_lines[-1].strip().lower()
        audio_copy = audio_codec.startswith("mp4a")

    return url_lines, audio_copy, result.stderr


def resolve_stream_url(video_id: str, max_res: str):
    """
    Resolve yt-dlp stream URLs with caching and in-flight deduplication.
    Returns (urls, audio_copy, stderr).
    """
    res = max_res or "720"

    # Fast path: cache hit
    cached = _get_cached(video_id, res)
    if cached is not None:
        log.info("Cache hit for %s@%sp", video_id, res)
        return cached["urls"], cached["audio_copy"], ""

    # Acquire or join an in-flight resolution for this key
    key = _cache_key(video_id, res)
    with _in_flight_lock:
        if key in _in_flight:
            event = _in_flight[key]
            is_resolver = False
        else:
            event = threading.Event()
            _in_flight[key] = event
            is_resolver = True

    if not is_resolver:
        # Wait for the resolver thread; then use its cached result
        log.info("Waiting for in-flight resolution of %s@%sp", video_id, res)
        event.wait(timeout=35)
        cached = _get_cached(video_id, res)
        if cached is not None:
            return cached["urls"], cached["audio_copy"], ""
        # Resolver failed — fall through to our own attempt (no dedup this time)
        urls, audio_copy, stderr = _do_resolve(video_id, res)
        if urls:
            _set_cached(video_id, res, urls, audio_copy)
        return urls, audio_copy, stderr

    # We are the resolver
    try:
        urls, audio_copy, stderr = _do_resolve(video_id, res)
        if urls:
            _set_cached(video_id, res, urls, audio_copy)
        return urls, audio_copy, stderr
    finally:
        with _in_flight_lock:
            _in_flight.pop(key, None)
        event.set()


def get_info(video_id: str):
    """Run yt-dlp -j to get video metadata JSON."""
    cmd = [YTDLP, "--quiet", "--no-warnings", "--no-playlist", "-j"]
    if COOKIES_FILE:
        cmd += ["--cookies", COOKIES_FILE]
    cmd += [f"https://www.youtube.com/watch?v={video_id}"]
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=30, cwd="/tmp")
    if result.returncode != 0:
        raise RuntimeError(result.stderr[:300])
    return json.loads(result.stdout)


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
            urls, audio_copy, stderr = resolve_stream_url(video_id, max_res)
        except subprocess.TimeoutExpired:
            log.error("yt-dlp timed out for %s", video_id)
            self.send_error(504, "yt-dlp timed out")
            return
        except Exception as e:
            log.error("yt-dlp error for %s: %s", video_id, e)
            self.send_error(500, str(e))
            return

        if not urls:
            log.error("No URL for %s — stderr: %s", video_id, stderr[:300])
            self.send_error(500, "No stream URL available")
            return

        # Build ffmpeg input args — one URL for combined, two for DASH (video+audio)
        ffmpeg_inputs = []
        for u in urls[:2]:
            ffmpeg_inputs += [
                # Reconnect on CDN drops without killing the stream
                "-reconnect", "1",
                "-reconnect_streamed", "1",
                "-reconnect_delay_max", "5",
                "-i", u,
            ]

        if len(urls) >= 2:
            # DASH: map both streams
            ffmpeg_map = ["-map", "0:v:0", "-map", "1:a:0"]
        else:
            ffmpeg_map = []

        # Use audio copy when source is already AAC — eliminates re-encoding CPU cost.
        # Fall back to AAC encode for non-AAC sources (Opus, Vorbis) which can't be
        # muxed directly into MPEG-TS.
        audio_codec = "copy" if audio_copy else "aac"
        log.info(
            "Piping %s via ffmpeg (%d input(s), audio=%s)",
            video_id, len(urls[:2]), audio_codec,
        )

        ffmpeg_cmd = (
            [
                FFMPEG, "-hide_banner", "-loglevel", "error",
                # Reduce input probing — default is 5MB/5s which adds startup latency.
                # 500KB is plenty to detect H.264/AAC in a known-format DASH stream.
                "-probesize", "500000",
                "-analyzeduration", "500000",
                # Disable internal output buffer so bytes reach Jellyfin immediately.
                "-fflags", "+nobuffer",
            ]
            + ffmpeg_inputs
            + ffmpeg_map
            + [
                "-c:v", "copy", "-c:a", audio_codec,
                # Drop data/subtitle streams — YouTube sometimes embeds chapter or
                # metadata streams; attempting to mux them into MPEG-TS wastes cycles.
                "-dn", "-sn",
                # Preserve source timestamps instead of renormalizing them.
                # Prevents drift and reduces timestamp processing work.
                "-copyts",
                # Tolerate minor CDN stream errors (timestamp gaps, missing frames)
                # instead of halting the stream.
                "-err_detect", "ignore_err",
                # Increase mux queue — prevents dropped packets when DASH video/audio
                # timestamps diverge briefly (common at stream start).
                "-max_muxing_queue_size", "1024",
                "-f", "mpegts", "pipe:1",
            ]
        )

        try:
            proc = subprocess.Popen(
                ffmpeg_cmd,
                stdout=subprocess.PIPE,
                cwd="/tmp",
            )
        except Exception as e:
            log.error("ffmpeg launch failed for %s: %s", video_id, e)
            self.send_error(500, f"ffmpeg launch failed: {e}")
            return

        self.send_response(200)
        self.send_header("Content-Type", "video/mp2t")
        self.send_header("Cache-Control", "no-cache")
        self.end_headers()

        try:
            shutil.copyfileobj(proc.stdout, self.wfile, 65536)
        except (BrokenPipeError, ConnectionResetError):
            log.info("Client disconnected for %s", video_id)
        finally:
            proc.kill()
            proc.wait()


if __name__ == "__main__":
    server = http.server.ThreadingHTTPServer(("127.0.0.1", PORT), StreamHandler)
    log.info("ytstream-proxy listening on http://127.0.0.1:%d", PORT)
    log.info("yt-dlp: %s | ffmpeg: %s", YTDLP, FFMPEG)
    log.info("URL cache TTL: %ds", URL_CACHE_TTL)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        server.shutdown()
