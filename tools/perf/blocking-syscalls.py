#!/usr/bin/env python3
"""Catch blocking syscalls on the Unity main thread and name the FILE.

Polls /proc/<tid>/syscall in a tight loop. Whenever the thread is inside a
syscall for longer than THRESH ms, resolves arg0 as an fd -> path and reports
the syscall number, duration and target. Read-only; no ptrace.
"""
import os, sys, time, collections

PID = int(sys.argv[1])
DUR = float(sys.argv[2]) if len(sys.argv) > 2 else 60.0
THRESH_MS = float(sys.argv[3]) if len(sys.argv) > 3 else 8.0

TID = PID
sysf = f"/proc/{PID}/task/{TID}/syscall"
fd_sys = os.open(sysf, os.O_RDONLY)
wch = f"/proc/{PID}/task/{TID}/wchan"

SYSNAME = {0: "read", 1: "write", 8: "lseek", 9: "mmap", 10: "mprotect",
           11: "munmap", 17: "pread64", 18: "pwrite64", 23: "select",
           74: "fsync", 75: "fdatasync", 202: "futex", 230: "clock_nanosleep",
           270: "pselect6", 271: "ppoll", 281: "epoll_wait", 425: "io_uring_setup",
           449: "futex_waitv", 87: "unlink", 257: "openat", 3: "close",
           262: "newfstatat", 16: "ioctl", 232: "epoll_wait", 7: "poll"}

def rdsys():
    try:
        return os.pread(fd_sys, 512, 0).decode().strip()
    except OSError:
        return ""

def resolve_fd(pid, fd):
    try:
        return os.readlink(f"/proc/{pid}/fd/{fd}")
    except OSError:
        return f"<fd {fd}?>"

events = []
cur = None      # (sysno, arg0, t_start)
t_end = time.monotonic() + DUR
agg = collections.Counter()

while time.monotonic() < t_end:
    s = rdsys()
    now = time.monotonic_ns()
    if not s or s.startswith("running") or s.startswith("-1"):
        if cur:
            dur = (now - cur[2]) / 1e6
            if dur >= THRESH_MS:
                events.append((cur[0], cur[1], dur, cur[3]))
            agg[cur[0]] += 1
            cur = None
        continue
    parts = s.split()
    try:
        sysno = int(parts[0])
    except ValueError:
        continue
    arg0 = parts[1] if len(parts) > 1 else "?"
    if cur is None or cur[0] != sysno or cur[1] != arg0:
        if cur:
            dur = (now - cur[2]) / 1e6
            if dur >= THRESH_MS:
                events.append((cur[0], cur[1], dur, cur[3]))
            agg[cur[0]] += 1
        path = ""
        if sysno in (0, 1, 17, 18, 74, 75, 16):
            try:
                path = resolve_fd(PID, int(arg0, 16))
            except Exception:
                path = ""
        cur = (sysno, arg0, now, path)

print(f"watched tid={TID} for {DUR}s, blocking events >= {THRESH_MS}ms: {len(events)}\n")
events.sort(key=lambda e: -e[2])
print(f"{'syscall':>16} {'dur_ms':>8}  target")
for sysno, arg0, dur, path in events[:40]:
    print(f"{SYSNAME.get(sysno, sysno):>16} {dur:8.1f}  {path or arg0}")

tot = collections.Counter()
for sysno, arg0, dur, path in events:
    tot[(SYSNAME.get(sysno, sysno), path)] += dur
print("\ntotal blocked ms by (syscall, target):")
for k, v in tot.most_common(15):
    print(f"  {v:9.1f}ms  {k}")
