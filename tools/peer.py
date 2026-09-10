#!/usr/bin/env python3
"""
Test peer for InscryptionMP.

Connects to the hosting game and stays connected, so the game's OpponentInjector sees
a live session when a match starts.

Auto mode (default): whenever the game passes the turn (sends END), the peer plays a
card and passes back. That lets a full match be played end to end without anyone
hand-feeding turns.

The peer also takes part in the lobby negotiation the way a second game would: it
mirrors whichever act the game votes for and presses start once the game has, so a
match can be brought up end to end from one machine.

    python tools/peer.py                 # agree with everything, start when they do
    python tools/peer.py --act 2         # insist on Act 2, to see a disagreement
    python tools/peer.py --wait          # never press start, to see 1/2 hold
    python tools/peer.py --legacy        # greet as protocol 3, to see the fallback
    python tools/peer.py --proto 4       # greet as protocol 4: no match rules
    python tools/peer.py --manual        # feed lines from .tmp/outbox.txt by hand
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

PROTOCOL = 5
MOD_VERSION = "1.5.0"


def log_in(line):
    with open(INBOX, "a", encoding="utf-8") as f:
        f.write(f"{time.strftime('%H:%M:%S')} <- {line}\n")
    print(f"<- {line}", flush=True)


def send(writer, msg, tag=""):
    writer.write(msg + "\n")
    writer.flush()
    print(f"-> {msg}{tag}", flush=True)


def reader(sock, writer, auto, opts):
    f = sock.makefile("r", encoding="utf-8", newline="\n")
    turn = 0
    ready = False
    for line in f:
        line = line.strip()   # game sends CRLF; a stray CR breaks equality checks
        if not line:
            continue
        log_in(line)

        if line.startswith("OVER"):
            print(f"*** MATCH OVER - peer reports: {line} ***", flush=True)
            ready = False
            continue

        # Greet back, so the client can verify us like a real peer would.
        if line.startswith("HELLO"):
            proto = opts["proto"]
            if proto < 4:
                send(writer, f"HELLO {proto} 1.2.0", "  (auto)")
                continue
            send(writer, f"HELLO {proto} {MOD_VERSION}", "  (auto)")
            send(writer, f"VOTE {opts['act'] or 1}", "  (auto)")
            send(writer, "READY 0", "  (auto)")
            continue

        # The host's house rules. A joining client just plays by them; there is nothing
        # to answer, and answering would let us overwrite what the host set.
        if line.startswith("RULES "):
            print(f"*** RULES FROM HOST: {line[6:]} ***", flush=True)
            continue

        if line.startswith("START "):
            print(f"*** MATCH STARTING - {line} ***", flush=True)
            ready = False
            continue

        if line.startswith("VOTE ") and opts["proto"] >= 4:
            # Agree by default, so a match can be brought up from one machine. --act
            # pins us to one instead, which is how you see a 1/2 disagreement.
            want = opts["act"] or line.split(" ", 1)[1].strip()
            send(writer, f"VOTE {want}", "  (auto)")
            continue

        if line.startswith("READY ") and opts["proto"] >= 4:
            theirs = line.split(" ", 1)[1].strip() == "1"
            if opts["wait"]:
                continue
            if theirs and not ready:
                time.sleep(0.5)
                ready = True
                send(writer, "READY 1", "  (auto)")
            elif not theirs and ready:
                ready = False
                send(writer, "READY 0", "  (auto)")
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
    proto = PROTOCOL
    if "--legacy" in sys.argv:
        proto = 3
    if "--proto" in sys.argv:
        proto = int(sys.argv[sys.argv.index("--proto") + 1])

    opts = {
        "proto": proto,
        "wait": "--wait" in sys.argv,
        "act": sys.argv[sys.argv.index("--act") + 1] if "--act" in sys.argv else None,
    }
    print(f"greeting as protocol {proto}", flush=True)

    # Wait for the game rather than requiring it to be hosting first, and go back to
    # waiting when it goes away. Testing means restarting the game over and over, and a
    # peer that has to be restarted alongside it is a worse tool than one that waits.
    while True:
        print(f"waiting for {HOST}:{PORT} ...", flush=True)
        while True:
            try:
                sock = socket.create_connection((HOST, PORT), timeout=5)
                break
            except KeyboardInterrupt:
                return
            except OSError:
                time.sleep(1.0)

        sock.settimeout(None)  # connect timeout must not linger on recv
        print(f"CONNECTED - auto-play {'ON' if auto else 'OFF'}", flush=True)

        link = threading.Thread(
            target=reader,
            args=(sock, sock.makefile("w", encoding="utf-8", newline="\n"), auto, opts),
            daemon=True)
        link.start()

        try:
            pump_outbox(sock, link)
        except KeyboardInterrupt:
            sock.close()
            return
        except (BrokenPipeError, OSError) as e:
            print(f"peer disconnected: {e}", flush=True)

        sock.close()
        print("--- game went away; waiting for it to come back ---", flush=True)


def pump_outbox(sock, link):
    """Sends anything appended to the outbox, until the link to the game drops."""
    writer = sock.makefile("w", encoding="utf-8", newline="\n")
    sent = 0
    while link.is_alive():
        with open(OUTBOX, "r", encoding="utf-8") as f:
            lines = [l.rstrip("\n") for l in f if l.strip()]
        while sent < len(lines):
            send(writer, lines[sent])
            sent += 1
        time.sleep(0.25)


if __name__ == "__main__":
    main()
