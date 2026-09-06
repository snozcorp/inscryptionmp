using System.Collections;
using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// Adds a MULTIPLAYER card to the title screen, alongside New Game and the rest.
    /// </summary>
    [HarmonyPatch]
    internal static class MenuCardEntry
    {
        private const string CardName = "InscryptionMP_MultiplayerCard";

        /// <summary>Ours, once built. Cleared whenever the menu is rebuilt.</summary>
        private static MenuCard _card;

        [HarmonyPatch(typeof(MenuController), "Start")]
        [HarmonyPostfix]
        private static void AddCard(MenuController __instance)
        {
            Trace.Info("[menucard] MenuController.Start - queueing card");
            __instance.StartCoroutine(AddCardWhenReady(__instance));
        }

        /// <summary>The reliable hook.</summary>
        [HarmonyPatch(typeof(MenuController), nameof(MenuController.TweenInCards))]
        [HarmonyPrefix]
        private static void AddBeforeTween(MenuController __instance)
        {
            Trace.Info("[menucard] TweenInCards - ensuring our card exists");
            Build(__instance);
        }

        /// <summary>
        /// Waits a frame first: the scene's own cards are wired up in Start too, and there
        /// is nothing to clone until they exist.
        /// </summary>
        private static IEnumerator AddCardWhenReady(MenuController controller)
        {
            yield return null;
            Build(controller);
        }

        private static void Build(MenuController controller)
        {
            try
            {
                if (!IsTitleScreen(controller)) return;

                var cards = AccessTools.Field(typeof(MenuController), "cards")
                                       ?.GetValue(controller) as List<MenuCard>;
                if (cards == null || cards.Count == 0)
                {
                    Trace.Warn("[menucard] no menu cards to clone from");
                    return;
                }

                // Already there - the menu can be rebuilt without the scene reloading.
                if (cards.Exists(c => c != null && c.name == CardName)) return;

                DropHiddenAscensionCard(cards);
                Trace.Info("[menucard] row: " + Describe(cards));

                MenuCard template = PickTemplate(cards);
                if (template == null) return;

                MenuCard clone = Object.Instantiate(template, template.transform.parent);
                clone.name = CardName;

                // ReInitPosition, not just transform: StartPosition is captured in Awake,
                // so the clone had the template's. Anything that returns cards to their
                // places - which the menu does constantly - snapped ours onto Options.
                Vector3 spot = NextPosition(cards);
                clone.transform.localPosition = spot;
                clone.ReInitPosition(spot);

                Retitle(clone, "Multiplayer");
                TintBorder(clone, new Color(0.95f, 0.35f, 0.28f));

                // Each menu card has its own artwork, so the clone was wearing the Options
                // card's face. Give it art the title screen isn't already using.
                GiveDistinctArt(clone, cards);

                cards.Add(clone);
                _card = clone;
                CentreRow(cards);
                Trace.Info("[menucard] added the multiplayer card at x=" + spot.x.ToString("0.00"));
            }
            catch (System.Exception e)
            {
                // A missing card is a shame; a broken title screen is unplayable.
                Trace.Error($"[menucard] could not add the card: {e.Message}");
            }
        }

        /// <summary>
        /// Whether this controller is the title screen rather than the in-game pause menu.
        /// </summary>
        private static bool IsTitleScreen(MenuController controller)
        {
            PauseMenu pause = PauseMenu.instance;
            if (pause != null)
            {
                var owned = AccessTools.Field(typeof(PauseMenu), "menuController")
                                       ?.GetValue(pause) as MenuController;
                if (owned != null && owned == controller)
                {
                    Trace.Info("[menucard] that controller is the pause menu - leaving it alone");
                    return false;
                }
            }

            // Belt and braces for the moment before PauseMenu.Awake has run: no title
            // screen offers to concede a run or to return to itself.
            var cards = AccessTools.Field(typeof(MenuController), "cards")
                                   ?.GetValue(controller) as List<MenuCard>;
            if (cards != null && cards.Exists(c => c != null &&
                    (c.MenuAction == MenuAction.Concede ||
                     c.MenuAction == MenuAction.ReturnToStartMenu)))
            {
                Trace.Info("[menucard] that row concedes or exits a run - not the title screen");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Drops the Kaycee's Mod card when it isn't unlocked, before we measure the row.
        /// </summary>
        private static void DropHiddenAscensionCard(List<MenuCard> cards)
        {
            if (StoryEventsData.EventCompleted(StoryEvent.ChapterSelectUnlocked)) return;

            int gone = cards.RemoveAll(c => c != null && c.MenuAction == MenuAction.EnterAscension);
            if (gone > 0) Trace.Info("[menucard] ignoring " + gone + " locked ascension card(s)");
        }

        private static string Describe(List<MenuCard> cards)
        {
            var sb = new System.Text.StringBuilder();
            foreach (MenuCard c in cards)
            {
                if (c == null) continue;
                sb.Append(c.MenuAction).Append("@")
                  .Append(c.transform.localPosition.x.ToString("0.00")).Append(" ");
            }
            return sb.ToString();
        }

        /// <summary>The gap between neighbouring cards in the row.</summary>
        private static float RowPitch(List<MenuCard> cards)
        {
            var xs = new List<float>();
            foreach (MenuCard c in cards)
                if (c != null) xs.Add(c.transform.localPosition.x);

            xs.Sort();

            float pitch = float.MaxValue;
            for (int i = 1; i < xs.Count; i++)
            {
                float gap = xs[i] - xs[i - 1];
                if (gap > 0.05f && gap < pitch) pitch = gap;
            }

            return pitch == float.MaxValue ? 1.2f : pitch;
        }

        /// <summary>
        /// Something ordinary to copy. Ascension and locked cards carry extra state and
        /// their own visuals, so they make a poor starting point.
        /// </summary>
        private static MenuCard PickTemplate(List<MenuCard> cards)
        {
            MenuCard best = null;
            foreach (MenuCard c in cards)
            {
                if (c == null || c.IsAscensionCard || c.Locked) continue;
                if (c.MenuAction == MenuAction.Options) return c;
                if (best == null) best = c;
            }
            if (best == null) Trace.Warn("[menucard] no suitable card to clone");
            return best;
        }

        /// <summary>Places the card past the right-hand end of the row.</summary>
        private static Vector3 NextPosition(List<MenuCard> cards)
        {
            MenuCard rightmost = null;
            float maxX = float.MinValue;

            foreach (MenuCard c in cards)
            {
                if (c == null) continue;
                float x = c.transform.localPosition.x;
                if (x > maxX) { maxX = x; rightmost = c; }
            }

            if (rightmost == null) return Vector3.zero;

            Vector3 pos = rightmost.transform.localPosition;
            return new Vector3(pos.x + RowPitch(cards), pos.y, pos.z);
        }

        /// <summary>
        /// Clears the copied title art so the controller renders our text instead, and
        /// clears the lock flags the template may have carried over.
        /// </summary>
        private static void Retitle(MenuCard card, string title)
        {
            Set(card, "titleSprite", null);
            Set(card, "lockedTitleSprite", null);
            Set(card, "titleText", title);
            Set(card, "titleLocId", "");
            Set(card, "permanentlyLocked", false);
            Set(card, "lockBeforeStoryEvent", false);
            Set(card, "lockAfterStoryEvent", false);
            Set(card, "isAscensionCard", false);
        }

        /// <summary>
        /// Marks the card out from the row. Every menu card shares the same face art and only
        /// shows its title on hover, so without this ours is indistinguishable.
        /// </summary>
        private static void TintBorder(MenuCard card, Color c)
        {
            Set(card, "defaultBorderColor", c);
            card.SetBorderColor(c);
        }

        /// <summary>
        /// Warms the card face so it isn't an exact copy of the one it was cloned from.
        /// </summary>
        private static void TintFace(MenuCard card, Color tint)
        {
            var renderers = card.GetComponentsInChildren<SpriteRenderer>(true);
            var seen = new System.Text.StringBuilder();

            foreach (SpriteRenderer r in renderers)
            {
                if (r == null) continue;
                seen.Append(r.name).Append(" | ");

                // The border carries its own colour and is handled separately.
                if (r.name.ToLowerInvariant().Contains("border")) continue;
                r.color = tint;
            }

            Trace.Info($"[menucard] renderers: {seen}");
        }

        /// <summary>Slides the whole row back so it stays centred.</summary>
        private static void CentreRow(List<MenuCard> cards)
        {
            if (cards.Count < 2) return;

            float shift = -RowPitch(cards) * 0.5f;

            foreach (MenuCard c in cards)
            {
                if (c == null) continue;
                Vector3 pos = c.transform.localPosition;
                pos.x += shift;
                c.transform.localPosition = pos;
                c.ReInitPosition(pos);
            }

            Trace.Info($"[menucard] recentred the row by {shift:0.00}");
        }

        /// <summary>
        /// Puts a face on the card that no other card on this screen is wearing.
        /// </summary>
        private static void GiveDistinctArt(MenuCard clone, List<MenuCard> cards)
        {
            var mine = clone.GetComponent<SpriteRenderer>();
            if (mine == null) return;

            var inUse = new List<string>();
            foreach (MenuCard c in cards)
            {
                if (c == null) continue;
                var r = c.GetComponent<SpriteRenderer>();
                if (r?.sprite != null) inUse.Add(r.sprite.name);
            }

            // Only two faces are genuinely spare; the rest are already on this screen or
            // are greyed/flash states of another card rather than faces of their own.
            string[] preferred = { "menucard_concede", "menucard_library" };

            Sprite chosen = null;
            Sprite fallback = null;
            var available = new System.Text.StringBuilder();

            foreach (Sprite sp in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sp == null || !sp.name.StartsWith("menucard_")) continue;
                if (sp.name.Contains("_greyed") || sp.name.Contains("_flash")) continue;

                available.Append(sp.name).Append(" | ");
                if (inUse.Contains(sp.name)) continue;

                if (System.Array.IndexOf(preferred, sp.name) == 0) { chosen = sp; break; }
                if (chosen == null && System.Array.IndexOf(preferred, sp.name) > 0) chosen = sp;
                if (fallback == null) fallback = sp;
            }

            if (chosen == null) chosen = fallback;

            Trace.Info($"[menucard] menucard sprites: {available}");

            if (chosen == null)
            {
                // Nothing spare: fall back to marking the Options face with a gold wash.
                Trace.Warn("[menucard] no unused card art - tinting instead");
                TintFace(clone, new Color(1f, 0.66f, 0.16f));
                return;
            }

            // Repaint it as our own card: dark red, with VS stamped in the middle. Built
            // from the real art, so the frame and paper grain are the game's own.
            Sprite versus = VersusCardArt.Build(chosen);

            mine.sprite = versus ?? chosen;
            mine.color = versus != null ? Color.white : new Color(0.72f, 0.20f, 0.16f);
            Trace.Info($"[menucard] using '{chosen.name}' as the card face");
        }

        private static void Set(MenuCard card, string field, object value)
        {
            var f = AccessTools.Field(typeof(MenuCard), field);
            if (f == null) { Trace.Warn($"[menucard] MenuCard.{field} not found"); return; }
            f.SetValue(card, value);
        }

        /// <summary>
        /// Opens the versus panel instead of running a menu action, then hands the menu back to
        /// the player.
        /// </summary>
        [HarmonyPatch(typeof(MenuController), "OnCardReachedSlot")]
        [HarmonyPrefix]
        private static bool InterceptOurCard(MenuController __instance, MenuCard card)
        {
            if (card == null || _card == null || card != _card) return true;

            Trace.Info("[menucard] multiplayer card slotted - opening the panel");

            try
            {
                // The flag the skipped method would have cleared.
                AccessTools.Property(typeof(MenuController), "DoingCardTransition")
                          ?.SetValue(__instance, false, null);

                // Pop the card back out and re-enable the row, so the title screen is
                // still usable behind our panel.
                __instance.ResetToDefaultState();
            }
            catch (System.Exception e)
            {
                Trace.Error($"[menucard] could not restore the menu: {e.Message}");
            }

            MpMenu.OpenFromMenuCard();
            return false;   // don't run a vanilla action
        }
    }
}
