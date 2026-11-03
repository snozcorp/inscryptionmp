using System.Collections;
using DiskCardGame;

namespace InscryptionMP
{
    /// <summary>
    /// Puts our scales where the peer's are. Each client weighs its own copy of every combat,
    /// so anything simulated differently on the two screens used to leave the score apart
    /// for the rest of the match. See Protocol.Scale for why the sender is believed.
    /// </summary>
    internal static class ScaleSync
    {
        /// <summary>Corrections made this match, for the log and the match summary.</summary>
        public static int Corrections { get; private set; }

        public static void Reset() => Corrections = 0;

        public static IEnumerator Reconcile(int theirBalance)
        {
            var life = Singleton<LifeManager>.Instance;
            if (life == null) yield break;

            int wanted = -theirBalance;
            int delta = wanted - life.Balance;
            if (delta == 0)
            {
                Trace.Info($"[scale] agreed at {life.Balance}");
                yield break;
            }

            Corrections++;
            Trace.Warn($"[scale] we had {life.Balance}, peer has {wanted} for us - moving {delta:+#;-#}");

            // Weights, not a bare number: Act 1's scale is physical and should lean to match.
            if (delta > 0) yield return life.ShowDamageSequence(delta, delta, toPlayer: false);
            else yield return life.ShowDamageSequence(-delta, -delta, toPlayer: true);

            if (life.Balance != wanted)
                Trace.Error($"[scale] still {life.Balance} after correcting towards {wanted}");
        }
    }
}
