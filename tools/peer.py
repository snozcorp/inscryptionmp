#!/usr/bin/env python3
"""
Fake peer for InscryptionMP.

Lets us drive the network opponent without a second copy of the game.
In-game: press F9 (host), then run this. It connects and plays a turn.

  python tools/peer.py                      # default scripted turn
  python tools/peer.py Wolf:0 Adder:2       # play Wolf in slot 0, Adder in slot 2
"""
import socket
import sys
import time

HOST, PORT = "127.0.0.1", 27333


def main():
    plays = []
    for arg in sys.argv[1:]:
        name, _, slot = arg.partition(":")
        plays.append((name, int(slot or 0)))
    if not plays:
        plays = [("Wolf", 1), ("Adder", 2)]

    print(f"connecting to {HOST}:{PORT} ...")
    s = socket.create_connection((HOST, PORT), timeout=10)
    print("connected. peer is now driving the opponent side.")

    f = s.makefile("rw", encoding="utf-8", newline="\n")

    for name, slot in plays:
        msg = f"PLAY {name} {slot}"
        print(f"-> {msg}")
        f.write(msg + "\n")
        f.flush()
        time.sleep(1.2)

    print("-> END")
    f.write("END\n")
    f.flush()

    # Drain anything the game sends back (our own plays echo here).
    s.settimeout(2.0)
    try:
        while True:
            line = f.readline()
            if not line:
                break
            print(f"<- {line.rstrip()}")
    except socket.timeout:
        pass

    print("done. (leaving socket open so the session stays alive)")
    try:
        while True:
            line = f.readline()
            if not line:
                break
            print(f"<- {line.rstrip()}")
    except KeyboardInterrupt:
        pass
    s.close()


if __name__ == "__main__":
    main()
