using System;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// The stretch between "connected" and "playing". Both clients run their own battle on
    /// their own table, so a match only makes sense if both players chose the same act and
    /// both committed to starting it - which is what this negotiates.
    /// </summary>
    internal static class Lobby
    {
        // Peer messages are parsed on the TCP reader thread while the menu reads these
        // every frame, so the shared ones are plain volatile fields. An int rather than a
        // MatchAct? because a nullable is two fields, and half of one is worse than none.
        private static volatile int _peerAct;
        private static volatile bool _peerReady;
        private static volatile bool _myReady;

        /// <summary>The act the peer wants, or null until they have said.</summary>
        public static MatchAct? PeerAct
        {
            get { int a = _peerAct; return a == 0 ? (MatchAct?)null : (MatchAct)a; }
        }

        /// <summary>Our own vote is simply whichever act the deck section has selected.</summary>
        public static MatchAct MyAct => ActInfo.Selected;

        public static bool MyReady => _myReady;
        public static bool PeerReady => _peerReady;

        /// <summary>
        /// Set whenever our vote or commitment changes. Nothing sends from where it changed:
        /// peer messages are parsed on the TCP reader thread, and Steam's API has to be
        /// called from Unity's, so <see cref="Tick"/> does all the sending.
        /// </summary>
        private static volatile bool _announcePending;

        /// <summary>When we last told the peer what we want, so a lost announcement retries.</summary>
        private static int _announcedAt;

        private const int ReannounceMs = 2000;

        /// <summary>
        /// Whether the peer takes part in the vote. When they don't, the old behaviour stands:
        /// either player starts, and both are pulled in.
        ///
        /// Only their own greeting can say so. This used to give up on a silent peer after a
        /// few seconds, which meant a dropped packet quietly turned the start button back
        /// into one that drags them into a match they never agreed to - and the retry below
        /// already covers a lost announcement. Waiting is the safe failure.
        /// </summary>
        public static bool Negotiating
        {
            get
            {
                if (!Net.Connected) return false;
                if (PeerAct.HasValue) return true;   // they already voted, so they can
                return Net.PeerSpeaksV4;             // otherwise their greeting decides
            }
        }

        public static bool ActAgreed => PeerAct.HasValue && PeerAct.Value == MyAct;

        /// <summary>How many of the two players want a given act. Drawn under the act buttons.</summary>
        public static int VotesFor(MatchAct act)
        {
            int votes = MyAct == act ? 1 : 0;
            if (PeerAct.HasValue && PeerAct.Value == act) votes++;
            return votes;
        }

        public static int ReadyCount => (MyReady ? 1 : 0) + (PeerReady ? 1 : 0);
        public static bool BothReady => MyReady && PeerReady;

        /// <summary>
        /// Why the start button is dead, phrased for the player, or null when it isn't.
        /// </summary>
        public static string Blocker
        {
            get
            {
                if (Net.HandshakeError != null) return "Both players need the same build of the mod.";
                if (!Net.Connected) return "No opponent connected.";
                if (!DeckStore.IsValid)
                    return "Your " + ActInfo.Name(MyAct) + " deck needs "
                           + DeckStore.MinCards + "-" + DeckStore.MaxCards + " cards.";
                if (!Negotiating) return null;
                if (!PeerAct.HasValue) return "Waiting for your opponent to pick an act.";
                if (!ActAgreed)
                    return "They want " + ActInfo.Name(PeerAct.Value) + ". Agree on one act to start.";
                return null;
            }
        }

        /// <summary>Whether pressing start right now would do anything.</summary>
        public static bool CanReady => Net.Connected && Blocker == null;

        // ------------------------------------------------------------------ our side

        /// <summary>Casting a vote. Changing it is also un-committing from the old one.</summary>
        public static void ChooseAct(MatchAct act)
        {
            bool changed = ActInfo.Selected != act;
            ActInfo.Selected = act;
            SteamTransport.PublishSelectedAct();   // keep a hosted lobby honest

            if (changed && MyReady) SetReady(false);
            _announcePending = true;
        }

        public static void ToggleReady()
        {
            if (!MyReady && !CanReady) return;   // un-committing is always allowed
            SetReady(!MyReady);
        }

        private static void SetReady(bool ready)
        {
            if (_myReady == ready) return;
            _myReady = ready;
            _announcePending = true;
            Trace.Info("[lobby] we are " + (ready ? "ready" : "no longer ready"));
        }

        // ---------------------------------------------------------------- their side

        public static void NotePeerAct(MatchAct act)
        {
            _peerAct = (int)act;
            Trace.Info("[lobby] opponent wants " + ActInfo.Name(act));

            // Their vote moving away from ours cancels the match we had agreed on, so our
            // own commitment to it goes with it.
            if (!ActAgreed && MyReady) SetReady(false);
        }

        public static void NotePeerReady(bool ready)
        {
            _peerReady = ready;
            Trace.Info("[lobby] opponent is " + (ready ? "ready" : "no longer ready"));
        }

        // ----------------------------------------------------------------- lifecycle

        /// <summary>A fresh peer: nothing the last one said still counts.</summary>
        public static void ResetForNewPeer()
        {
            _peerAct = 0;
            _peerReady = false;
            _myReady = false;
            _announcePending = true;

            // A result belongs to the opponent it was won against. Carrying "You won" into
            // a lobby with somebody else is just untrue.
            VersusMode.ForgetLastResult();
        }

        public static void Reset() => ResetForNewPeer();

        /// <summary>
        /// Both sides step out of ready as the agreed match begins, so a rematch has to be
        /// asked for again rather than firing the moment the menu comes back.
        /// </summary>
        public static void NoteMatchStarting()
        {
            _myReady = false;
            _peerReady = false;
        }

        /// <summary>
        /// Pumped every frame from the menu. Everything that sends lives here so every send
        /// happens on Unity's thread, which Steam's API requires.
        /// </summary>
        public static void Tick(MonoBehaviour host)
        {
            if (!Net.Connected) return;
            if (VersusMode.InMatch || VersusMode.PendingStart) return;

            // Say it again every couple of seconds until they answer. A send can be dropped
            // by a transport that is connected but not yet carrying packets, and one lost
            // announcement would otherwise leave both sides waiting on each other forever.
            bool retry = !PeerAct.HasValue && Net.PeerSpeaksV4
                         && unchecked(Environment.TickCount - _announcedAt) > ReannounceMs;

            if (_announcePending || retry)
            {
                _announcePending = false;
                _announcedAt = Environment.TickCount;
                Net.Send(Protocol.Vote(MyAct));
                Net.Send(Protocol.Ready(MyReady));
            }

            // One side decides. If both fired, two START messages would cross and each
            // client would try to start a match it was already starting.
            if (Net.IsHost && Negotiating && ActAgreed && BothReady)
            {
                Trace.Info("[lobby] both players are ready - starting the match");
                VersusMode.StartAnywhere(host);
            }
        }
    }
}
