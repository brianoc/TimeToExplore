#!/usr/bin/env python3
"""Run a command under a health check.

Unity in batch mode can keep running after it has finished its work. The known
failure is a retry loop while it tries to reach its cloud endpoints, which keeps
the process (and the shell waiting on it) alive indefinitely. This wrapper stops
the command when:

* it writes nothing to the log for ``--stall-seconds``;
* the log tail is a Unity cloud retry loop (many ``Could not resolve`` lines);
* ``Application is shutting down`` appears and shutdown does not finish within
  ``--shutdown-grace`` seconds;
* the whole run exceeds ``--timeout-seconds``.

The command runs in its own process group, so a stop also takes down Unity's
asset-import workers. A stop exits 124, the same code as GNU ``timeout``; any
other exit code is the command's own.

    python3 tools/unity_guard.py --log /tmp/unity.log -- Unity -batchmode ...

Only the standard library is used, so it runs under any Python 3.
"""

import argparse
import os
import signal
import subprocess
import sys
import time

# A retry loop repeats these lines thousands of times; a single hiccup at
# startup is normal, so require a cluster in the tail.
RETRY_MARKERS = (b"Could not resolve",)
RETRY_THRESHOLD = 20
SHUTDOWN_MARKER = b"Application is shutting down"
TAIL_BYTES = 8192

# How long a stopped process gets to exit after SIGTERM before SIGKILL.
TERM_GRACE_SECONDS = 5.0


def read_tail(path, size):
    """Return the last bytes of *path*, or an empty string if it is unreadable."""
    try:
        with open(path, "rb") as handle:
            handle.seek(max(0, size - TAIL_BYTES))
            return handle.read()
    except OSError:
        return b""


def stop(process):
    """Terminate the process group, then kill it if it ignores SIGTERM."""
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except (ProcessLookupError, PermissionError):
        pass
    try:
        process.wait(timeout=TERM_GRACE_SECONDS)
        return
    except subprocess.TimeoutExpired:
        pass
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except (ProcessLookupError, PermissionError):
        pass
    try:
        process.wait(timeout=TERM_GRACE_SECONDS)
    except subprocess.TimeoutExpired:
        pass


def supervise(command, log_path, stall_seconds, timeout_seconds,
              shutdown_grace, poll_seconds):
    """Run *command* and return its exit code, or 124 if the guard stopped it."""
    start = time.monotonic()
    process = subprocess.Popen(command, start_new_session=True)

    last_size = -1
    last_growth = start
    shutdown_seen = None
    reason = None

    while True:
        code = process.poll()
        if code is not None:
            return code

        now = time.monotonic()
        if timeout_seconds and now - start >= timeout_seconds:
            reason = (f"the run exceeded its {timeout_seconds:g} s time budget")
            break

        tail = b""
        size = -1
        if log_path:
            try:
                size = os.path.getsize(log_path)
            except OSError:
                size = 0
        if size != last_size:
            last_size = size
            last_growth = now
            tail = read_tail(log_path, size)

            retries = sum(tail.count(marker) for marker in RETRY_MARKERS)
            if retries >= RETRY_THRESHOLD:
                reason = (f"it is stuck retrying a Unity cloud lookup "
                          f"({retries} \"Could not resolve\" lines in the log tail)")
                break

            if SHUTDOWN_MARKER in tail and shutdown_seen is None:
                shutdown_seen = now
        elif shutdown_seen is not None and now - shutdown_seen >= shutdown_grace:
            reason = (f"shutdown did not finish within {shutdown_grace:g} s "
                      f"after \"{SHUTDOWN_MARKER.decode()}\"")
            break
        elif log_path and now - last_growth >= stall_seconds:
            reason = (f"it wrote nothing to the log for {stall_seconds:g} s")
            break

        time.sleep(poll_seconds)

    stop(process)
    where = f"; log: {log_path}" if log_path else ""
    print(f"unity_guard: stopped the run because {reason}{where}", file=sys.stderr)
    return 124


def parse_args(argv):
    parser = argparse.ArgumentParser(
        description="Run a command and stop it if it gets stuck.",
        epilog="Put the command after --, e.g. -- Unity -batchmode ...",
    )
    parser.add_argument("--log", help="log file to watch for progress and known hang signatures")
    parser.add_argument("--stall-seconds", type=float, default=120.0,
                        help="stop after this long with no log growth (default: 120)")
    parser.add_argument("--timeout-seconds", type=float, default=1800.0,
                        help="stop after this long in total; 0 disables (default: 1800)")
    parser.add_argument("--shutdown-grace", type=float, default=20.0,
                        help="seconds allowed after Unity starts shutting down (default: 20)")
    parser.add_argument("--poll-seconds", type=float, default=1.0,
                        help="how often to check (default: 1.0)")
    parser.add_argument("command", nargs=argparse.REMAINDER,
                        help="command to run, after --")
    args = parser.parse_args(argv)
    if args.command and args.command[0] == "--":
        args.command = args.command[1:]
    if not args.command:
        parser.error("no command given (put it after --)")
    return args


def main(argv=None):
    args = parse_args(sys.argv[1:] if argv is None else argv)
    return supervise(
        args.command,
        args.log,
        args.stall_seconds,
        args.timeout_seconds,
        args.shutdown_grace,
        args.poll_seconds,
    )


if __name__ == "__main__":
    sys.exit(main())
