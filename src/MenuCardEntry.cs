using System.Collections;
using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// Adds a MULTIPLAYER card to the title screen, alongside New Game and the rest.
    ///
    /// The start menu really is cards dropped into a slot - MenuCard, MenuSlot and a
    /// MenuAction switch - so the mod can live there as one more card rather than as an
    /// overlay bolted on top. Drag it into the slot and the versus panel opens.
    ///
    /// The card is cloned from one the scene already has, so it inherits the art, border,
    /// animation and collider without us shipping any assets. Its title is left as text:
    /// MenuController falls back to rendering TitleText when a card has no title sprite,
    /// in the game's own font, which is why this needs no hand-drawn word art.
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

        /// <summary>
        /// The reliable hook. Start fires once when the scene object wakes, which may be
        /// before the card list is populated or may already have happened; TweenInCards
        /// runs every time the menu is actually presented, which is exactly when the card
        /// needs to exist. Adding it here also means it animates in with the others.
        /// </summary>
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
                var cards = AccessTools.Field(typeof(MenuController), "cards")
                                       ?.GetValue(controller) as List<MenuCard>;
                if (cards == null || cards.Count == 0)
                {
                    Trace.Warn("[menucard] no menu cards to clone from");
                    return;
                }

                // Already there - the menu can be rebuilt without the scene reloading.
                if (cards.Exists(c => c != null && c.name == CardName)) return;

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
                Trace.Info("[menucard] added the multiplayer card to the title screen");
            }
            catch (System.Exception e)
            {
                // A missing card is a shame; a broken title screen is unplayable.
                Trace.Error($"[menucard] could not add the card: {e.Message}");
            }
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

        /// <summary>
        /// Places the card past the right-hand end of the row.
        ///
        /// Taking the step between the last two entries of the list was wrong: the list is
        /// not in left-to-right order, so on a menu with Kaycee's Mod in it the card landed
        /// on top of Options. Reading the actual positions is order-independent.
        /// </summary>
        private static Vector3 NextPosition(List<MenuCard> cards)
        {
            MenuCard rightmost = null;
            float minX = float.MaxValue, maxX = float.MinValue;

            foreach (MenuCard c in cards)
            {
                if (c == null) continue;
                float x = c.transform.localPosition.x;
                if (x < minX) minX = x;
                if (x > maxX) { maxX = x; rightmost = c; }
            }

            if (rightmost == null) return Vector3.zero;

            // Average gap across the row, so we match whatever spacing this menu uses.
            float spacing = cards.Count > 1 ? (maxX - minX) / (cards.Count - 1) : 0f;
            if (spacing < 0.05f) spacing = 1.2f;

            Vector3 pos = rightmost.transform.localPosition;
            return new Vector3(pos.x + spacing, pos.y, pos.z);
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
        /// Marks the card out from the row. Every menu card shares the same face art and
        /// only shows its title on hover, so without this ours is indistinguishable.
        ///
        /// Sets the stored default as well as the live colour: unslotting a card calls
        /// ResetBorderColor, so tinting only the renderer would wash out the first time
        /// anyone picked it up.
        /// </summary>
        private static void TintBorder(MenuCard card, Color c)
        {
            Set(card, "defaultBorderColor", c);
            card.SetBorderColor(c);
        }

        /// <summary>
        /// Warms the card face so it isn't an exact copy of the one it was cloned from.
        ///
        /// Every menu card shares the same art and only shows its title on hover, so a
        /// straight clone is indistinguishable from Options sitting next to it. Tinting
        /// the face rather than swapping the sprite keeps the frame, wear and lighting
        /// the scene already gives it.
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

        /// <summary>
        /// Slides the whole row back so it stays centred.
        ///
        /// Our card is added past the right-hand end, which pushes the row off-centre by
        /// half a slot. Every card moves by the same amount, so the spacing the scene was
        /// authored with is untouched.
        ///
        /// StartPosition moves too: it is where the menu returns cards to, so shifting only
        /// the transform would let them drift back the moment anything reset them.
        /// </summary>
        private static void CentreRow(List<MenuCard> cards)
        {
            int counted = 0;
            float minX = float.MaxValue, maxX = float.MinValue;

            foreach (MenuCard c in cards)
            {
                if (c == null) continue;
                float x = c.transform.localPosition.x;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                counted++;
            }
            if (counted < 2) return;

            float spacing = (maxX - minX) / (counted - 1);
            float shift = -spacing * 0.5f;

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
        ///
        /// MenuAction covers screens beyond the title - Library, EditDeck, Concede, EndRun -
        /// and their card art is loaded even though the title screen never shows it. Using
        /// one of those keeps the card hand-drawn and in-style rather than recoloured, and
        /// costs us no assets.
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
        /// Opens the versus panel instead of running a menu action, then hands the menu
        /// back to the player.
        ///
        /// Skipping the vanilla method outright left the menu stuck: it is what clears
        /// DoingCardTransition, and while that flag is set the controller ignores every
        /// input, so the card could not be taken out and no other card could go in. The
        /// housekeeping has to happen even though the action doesn't.
        ///
        /// Our card carries whatever MenuAction it was cloned from, so it is identified by
        /// instance - letting the vanilla switch see it would open Options.
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
