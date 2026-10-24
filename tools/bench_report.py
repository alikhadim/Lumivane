"""Builds docs/BENCHMARKS.md from the files written by scripts/benchmark.sh. Only recorded scenarios appear; nothing is estimated.
Usage: python3 tools/bench_report.py [benchmarks_dir] [output.md]"""
import csv, os, re, sys

root = sys.argv[1] if len(sys.argv) > 1 else "benchmarks"
out = sys.argv[2] if len(sys.argv) > 2 else "docs/BENCHMARKS.md"
ORDER = ["baseline", "desktop", "youtube-4k", "netflix", "vlc-4k-hdr", "gaming"]
TITLES = {"baseline": "Idle (sync off)", "desktop": "Desktop use", "youtube-4k": "YouTube 4K", "netflix": "Netflix",
          "vlc-4k-hdr": "VLC 4K HDR", "gaming": "Gaming"}
WARMUP = 5.0

def pct(v, p):
    v = sorted(v)
    return v[min(len(v) - 1, int(round(p / 100 * (len(v) - 1))))] if v else None

def stat(v):
    return (sum(v) / len(v), pct(v, 95), max(v)) if v else None

def fmt(s, i=0, nd=1):
    return "n/a" if s is None else f"{s[i]:.{nd}f}"

def read_info(d):
    info = {}
    p = os.path.join(d, "info.txt")
    if os.path.exists(p):
        for line in open(p):
            if "=" in line:
                k, v = line.strip().split("=", 1); info[k] = v
    return info

def read_metrics(d):
    if not os.path.exists(os.path.join(d, "metrics.csv")): return []
    rows = list(csv.DictReader(open(os.path.join(d, "metrics.csv"))))
    if not rows: return []
    tmax = float(rows[-1]["t_s"]); secs = float(read_info(d).get("seconds", 120))
    lo = max(tmax - secs, 0) + WARMUP
    return [r for r in rows if float(r["t_s"]) >= lo]

def col(rows, k, positive=False):
    v = [float(r[k]) for r in rows if r.get(k) not in (None, "")]
    return [x for x in v if x > 0] if positive else v

def gpu(d):
    """System-wide GPU device utilization (%) sampled once per second from the GPU driver."""
    p = os.path.join(d, "rss.csv")
    if not os.path.exists(p): return None
    v = [float(r["gpu_pct"]) for r in csv.DictReader(open(p)) if r.get("gpu_pct") not in (None, "")]
    return stat(v[int(WARMUP):] or v)

def cpu_from_ps(d):
    """CPU of the process as % of one core from cumulative CPU seconds, for scenarios where the sync engine is off."""
    p = os.path.join(d, "rss.csv")
    if not os.path.exists(p): return None
    r = [(float(x["t_s"]), float(x["cpu_s"])) for x in csv.DictReader(open(p)) if x.get("cpu_s")]
    r = [x for x in r if x[0] >= WARMUP]
    return None if len(r) < 2 or r[-1][0] <= r[0][0] else (r[-1][1] - r[0][1]) / (r[-1][0] - r[0][0]) * 100

def rss(d):
    p = os.path.join(d, "rss.csv")
    if not os.path.exists(p): return None
    v = [float(r["rss_mb"]) for r in csv.DictReader(open(p))]
    return stat(v[int(WARMUP):] or v)

def has(s):
    d = os.path.join(root, s)
    if not os.path.exists(os.path.join(d, "info.txt")): return False
    m = os.path.join(d, "metrics.csv")
    if read_info(d).get("sync") != "on" and os.path.exists(m) and len(open(m).read().splitlines()) > 1:
        print(f"skipped {s}: recorded as idle but the sync engine was running; record it again"); return False
    return True
scen = [s for s in ORDER if has(s)]
scen += sorted(s for s in os.listdir(root) if s not in ORDER and has(s)) if os.path.isdir(root) else []
lines = ["# Benchmarks", ""]
if not scen:
    lines += ["No measurements recorded yet.", "", "Record each scenario with `./scripts/benchmark.sh <scenario> 120 --gpu` and regenerate this file with `python3 tools/bench_report.py`.",
              "Scenarios: `desktop`, `youtube-4k`, `netflix`, `vlc-4k-hdr`, `gaming` (and `baseline` with `--no-sync`)."]
else:
    i0 = read_info(os.path.join(root, scen[0]))
    lines += [f"Machine: {i0.get('chip', '?')}, {i0.get('memory_gb', '?')} GB, macOS {i0.get('macos', '?')}. App version {i0.get('app_version', '?')}. Recorded {i0.get('date', '?')[:10]}.",
              f"Each scenario: {i0.get('seconds', '?')} s window after a {int(WARMUP)} s warm-up, default profile. Values are mean / 95th percentile / maximum unless noted.", ""]
    lines += ["| Scenario | CPU % of one core | Memory MB | GPU % utilization (system-wide) | Frames/s | Sync mode |", "|---|---|---|---|---|---|"]
    detail = []
    for s in scen:
        d = os.path.join(root, s); r = read_metrics(d); info = read_info(d)
        cpu = stat(col(r, "cpu_percent"))
        if cpu is None and cpu_from_ps(d) is not None: cpu = (cpu_from_ps(d), None, None)   # idle: one average over the window
        fps = stat(col(r, "capture_fps")); g = gpu(d); m = rss(d)
        cpu_txt = f"{fmt(cpu,0)} / {fmt(cpu,1)} / {fmt(cpu,2)}" if cpu and cpu[1] is not None else f"{fmt(cpu,0)} (mean)"
        lines.append(f"| {TITLES.get(s, s)} | {cpu_txt} | {fmt(m,0,0)} / {fmt(m,2,0)} (max) | {fmt(g,0)} / {fmt(g,2)} (max) | {fmt(fps,0)} | {info.get('mode') or 'off'} |")
        detail.append((s, r))
    lines += ["", "## Latency and pipeline time (ms)", "", "| Scenario | Screen capture | Color analysis | GPU per frame | Screen to bridge | Sound to bridge | Bridge round trip | Packets/s |", "|---|---|---|---|---|---|---|---|"]
    for s, r in detail:
        c = lambda k, pos=False: stat(col(r, k, pos))
        lines.append(f"| {TITLES.get(s, s)} | {fmt(c('capture_ms',True),0,1)} / {fmt(c('capture_ms',True),1,1)} | {fmt(c('reduce_ms',True),0,2)} | {fmt(c('gpu_ms',True),0,2)} | "
                     f"{fmt(c('end_to_end_ms',True),0,1)} / {fmt(c('end_to_end_ms',True),1,1)} | {fmt(c('audio_to_light_ms',True),0,1)} / {fmt(c('audio_to_light_ms',True),1,1)} | "
                     f"{fmt(c('network_ms',True),0,1)} | {fmt(c('send_hz'),0,0)} |")
lines += ["", "## Definitions and limits",
          "- CPU: process CPU time as a percentage of one core (100 = one full core), sampled by the app every 0.5 s.",
          "- Memory: resident set size of the app from `ps`.",
          "- GPU: device utilization reported by the GPU driver (`ioreg`, AGXAccelerator) once per second, for the whole system, not only this app. Compare with the idle row.",
          "- Screen capture: delay between the display presenting a frame and the app receiving it (ScreenCaptureKit). Frame timestamps can be slightly ahead of the host clock; samples at or below 0 are excluded.",
          "- Screen to bridge: frame presentation to the Entertainment packet leaving the Mac. Sound to bridge: audio buffer end to packet send.",
          "- Bridge round trip: REST request to the bridge, a proxy for local network and bridge response time.",
          "- Not measured: time from the bridge to visible light. That needs an external camera or photodiode.",
          "- Protected video (some streaming apps) is captured as black; the pipeline cost is still measured, the light output is not representative."]
os.makedirs(os.path.dirname(out), exist_ok=True)
open(out, "w").write("\n".join(lines) + "\n")
print(f"wrote {out} ({len(scen)} scenarios)")
