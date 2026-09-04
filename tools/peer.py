#!/usr/bin/env python3
"""
Test peer for InscryptionMP.

Connects to the hosting game and stays connected, so the game's OpponentInjector sees
a live session when a match starts.

Auto mode (default): whenever the game passes the turn (sends END), the peer plays a
card and passes back. That lets a full match be played end to end without anyone
hand-feeding turns.

Manual mode (--manual): append lines to .tmp/outbox.txt and they get sent.

    python tools/peer.py
    python tools/peer.py --manual
"""
import os
import socket
import sys
import threading
import time

HOST, PORT = "127.0.0.1", 27333
OUTBOX = os.path.join(".tmp", "outbox.txt")
INBOX = os.path.join(".tmp", "inbox.log")

AUTO_CARDS = ["Stoat", "Bullfrog", "Wolf", "Adder"]


def log_in(line):
    with open(INBOX, "a", encoding="utf-8") as f:
        f.write(f"{time.strftime('%H:%M:%S')} <- {line}\n")
    print(f"<- {line}", flush=True)


def send(writer, msg, tag=""):
    writer.write(msg + "\n")
    writer.flush()
    print(f"-> {msg}{tag}", flush=True)


def reader(sock, writer, auto):
    f = sock.makefile("r", encoding="utf-8", newline="\n")
    turn = 0
    for line in f:
        line = line.strip()   # game sends CRLF; a stray CR breaks equality checks
        if not line:
            continue
        log_in(line)

        if line.startswith("OVER"):
            print(f"*** MATCH OVER - peer reports: {line} ***", flush=True)
            continue

        if auto and line == "END":
            time.sleep(1.0)
            card = AUTO_CARDS[turn % len(AUTO_CARDS)]
            slot = turn % 4
            turn += 1
            send(writer, f"PLAY {card} {slot}", "  (auto)")
            time.sleep(0.6)

            # Protocol 3 board snapshots carry stats. Claim a deliberately silly
            # attack so the correction is obvious on screen if it is working.
            board = ["-"] * 4
            board[slot] = f"{card}:2/3:Sniper"
            send(writer, "BOARD " + "|".join(board), "  (auto)")
            time.sleep(0.2)

            # Our cards claim Sniper, so tell the peer where we aimed it.
            # Always slot 0, which is obvious to spot on screen.
            send(writer, f"AIM {slot} 0", "  (auto)")
            time.sleep(0.2)
            send(writer, "END", "  (auto)")


def main():
    os.makedirs(".tmp", exist_ok=True)
    open(OUTBOX, "w").close()
    open(INBOX, "w").close()

    auto = "--manual" not in sys.argv

    print(f"connecting to {HOST}:{PORT} ...", flush=True)
    sock = socket.create_connection((HOST, PORT), timeout=15)
    sock.settimeout(None)  # connect timeout must not linger on recv
    print(f"CONNECTED - auto-play {'ON' if auto else 'OFF'}", flush=True)

    writer = sock.makefile("w", encoding="utf-8", newline="\n")
    threading.Thread(target=reader, args=(sock, writer, auto), daemon=True).start()

    sent = 0
    try:
        while True:
            with open(OUTBOX, "r", encoding="utf-8") as f:
                lines = [l.rstrip("\n") for l in f if l.strip()]
            while sent < len(lines):
                send(writer, lines[sent])
                sent += 1
            time.sleep(0.25)
    except (KeyboardInterrupt, BrokenPipeError, OSError) as e:
        print(f"peer closing: {e}", flush=True)
    finally:
        sock.close()


if __name__ == "__main__":
    main()
