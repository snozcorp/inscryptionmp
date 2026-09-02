#!/usr/bin/env python3
"""
Persistent fake peer for InscryptionMP.

Connects to the hosting game and stays connected, so the game's OpponentInjector
sees a live session when a battle spawns. Acts as a file-driven bridge:

  - anything appended to .tmp/outbox.txt is sent to the game
  - anything the game sends is appended to .tmp/inbox.log

  python tools/peer.py            # connect and hold
  echo "PLAY Wolf 1" >> .tmp/outbox.txt
  echo "END"          >> .tmp/outbox.txt
"""
import os
import socket
import sys
import threading
import time

HOST, PORT = "127.0.0.1", 27333
OUTBOX = os.path.join(".tmp", "outbox.txt")
INBOX = os.path.join(".tmp", "inbox.log")


def reader(sock):
    f = sock.makefile("r", encoding="utf-8", newline="\n")
    for line in f:
        line = line.rstrip("\n")
        if not line:
            continue
        with open(INBOX, "a", encoding="utf-8") as log:
            log.write(f"{time.strftime('%H:%M:%S')} <- {line}\n")
        print(f"<- {line}", flush=True)


def main():
    os.makedirs(".tmp", exist_ok=True)
    open(OUTBOX, "w").close()
    open(INBOX, "w").close()

    print(f"connecting to {HOST}:{PORT} ...", flush=True)
    sock = socket.create_connection((HOST, PORT), timeout=15)
    sock.settimeout(None)  # connect timeout must not linger on recv
    print("CONNECTED - overlay should be green now.", flush=True)

    threading.Thread(target=reader, args=(sock,), daemon=True).start()

    writer = sock.makefile("w", encoding="utf-8", newline="\n")
    sent = 0
    try:
        while True:
            with open(OUTBOX, "r", encoding="utf-8") as f:
                lines = [l.rstrip("\n") for l in f if l.strip()]
            while sent < len(lines):
                msg = lines[sent]
                sent += 1
                writer.write(msg + "\n")
                writer.flush()
                print(f"-> {msg}", flush=True)
            time.sleep(0.25)
    except (KeyboardInterrupt, BrokenPipeError, OSError) as e:
        print(f"peer closing: {e}", flush=True)
    finally:
        sock.close()


if __name__ == "__main__":
    main()
