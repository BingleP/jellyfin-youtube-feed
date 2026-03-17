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

YTDLP        = os.environ.get("YTDLP_PATH", "/usr/bin/yt-dlp")
FFMPEG       = os.environ.get("FFMPEG_PATH", "/usr/lib/jellyfin-ffmpeg/ffmpeg")
PORT         = int(os.environ.get("PROXY_PORT", "3003"))
COOKIES_FILE = os.environ.get("COOKIES_FILE", "")

logging.basicConfig(level=logging.INFO, format="[%(asctime)s] %(levelname)s %(message)s")
log = logging.getLogger("ytstream-proxy")


def resolve_stream_url(video_id, max_res):
    """Run yt-dlp --get-url to get a direct stream URL."""
    res = max_res or "720"
    # Prefer H.264 (avc1) video — AV1/VP9 can't be stream-copied into MPEG-TS
    fmt = (
        f"bestvideo[height<={res}][vcodec^=avc1]+bestaudio"
        f"/bestvideo[height<={res}][vcodec=h264]+bestaudio"
        f"/best[height<={res}][vcodec^=avc1]"
        f"/best[height<={res}]"
        f"/best"
    )
    cmd = [YTDLP, "--quiet", "--no-warnings", "--no-playlist"]
    if COOKIES_FILE:
        cmd += ["--cookies", COOKIES_FILE]
    cmd += ["-f", fmt, "--get-url", f"https://www.youtube.com/watch?v={video_id}"]
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=30, cwd="/tmp")
    urls = result.stdout.strip().splitlines()
    # yt-dlp may return two URLs (video + audio) for DASH formats
    return urls, result.stderr


def get_info(video_id):
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
            urls, stderr = resolve_stream_url(video_id, max_res)
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
            ffmpeg_inputs += ["-i", u]

        if len(urls) >= 2:
            # DASH: map both streams
            ffmpeg_map = ["-map", "0:v:0", "-map", "1:a:0"]
        else:
            ffmpeg_map = []

        ffmpeg_cmd = (
            [FFMPEG, "-hide_banner", "-loglevel", "error"]
            + ffmpeg_inputs
            + ffmpeg_map
            + ["-c:v", "copy", "-c:a", "aac", "-f", "mpegts", "pipe:1"]
        )

        log.info("Piping %s via ffmpeg (%d input(s))", video_id, len(urls[:2]))

        try:
            proc = subprocess.Popen(
                ffmpeg_cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.DEVNULL,
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
            shutil.copyfileobj(proc.stdout, self.wfile)
        except (BrokenPipeError, ConnectionResetError):
            log.info("Client disconnected for %s", video_id)
        finally:
            proc.kill()
            proc.wait()


if __name__ == "__main__":
    server = http.server.ThreadingHTTPServer(("127.0.0.1", PORT), StreamHandler)
    log.info("ytstream-proxy listening on http://127.0.0.1:%d", PORT)
    log.info("yt-dlp: %s | ffmpeg: %s", YTDLP, FFMPEG)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        server.shutdown()
