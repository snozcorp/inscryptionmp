using System;
using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>Crash insurance for the card renderer.</summary>
    [HarmonyPatch]
    internal static class CardRenderFallbacks
    {
        /// <summary>World size of a normal 3D portrait, learned from the first real one.</summary>
        private static Vector2 _referenceSize = Vector2.zero;

        /// <summary>Rescaled pixel portraits, keyed by the sprite they stand in for.</summary>
        private static readonly Dictionary<Sprite, Sprite> Rescaled = new Dictionary<Sprite, Sprite>();

        /// <summary>
        /// Gives Act 2 cards a portrait when they're shown on the 3D card table.
        /// </summary>
        [HarmonyPatch(typeof(Card), nameof(Card.SetInfo))]
        [HarmonyPrefix]
        private static void SubstitutePixelPortrait(CardInfo info)
        {
            if (info == null) return;

            if (info.portraitTex != null)
            {
                if (_referenceSize == Vector2.zero) _referenceSize = info.portraitTex.bounds.size;

                // Some cards carry a portrait larger than the slot. On the 3D table it
                // spills past the frame and covers the cost row - Green Mage is the
                // obvious one. Shrink it to fit rather than letting it overlap.
                if (VersusMode.VersusContext) ClampOversizedPortrait(info);
                return;
            }

            // Only for the act that actually uses pixel art; elsewhere a missing 3D
            // portrait means the card doesn't belong on this table at all.
            if (!ActInfo.UsesPixelArt(ActInfo.Selected)) return;
            if (info.pixelPortrait == null) return;

            Sprite scaled = ScaleToPortraitSlot(info.pixelPortrait);
            if (scaled != null) info.portraitTex = scaled;
        }

        /// <summary>
        /// Rescales a 3D portrait that is bigger than the slot the card gives it. A little over
        /// is normal variation between portraits; well over means it will draw on top of the
        /// card's own frame and stats.
        /// </summary>
        private static void ClampOversizedPortrait(CardInfo info)
        {
            if (_referenceSize == Vector2.zero) return;

            Vector2 size = info.portraitTex.bounds.size;
            if (size.x <= _referenceSize.x * 1.05f && size.y <= _referenceSize.y * 1.05f) return;

            Sprite fitted = ScaleToPortraitSlot(info.portraitTex);
            if (fitted == null) return;

            Trace.Info($"[art] '{info.name}' portrait {size.x:0.00}x{size.y:0.00} overflows the " +
                       $"{_referenceSize.x:0.00}x{_referenceSize.y:0.00} slot - shrunk to fit");
            info.portraitTex = fitted;
        }

        /// <summary>Measures a normal 3D portrait so pixel art can be matched to it.</summary>
        private static void LearnReferenceSize()
        {
            foreach (string name in new[] { "Squirrel", "Stoat", "Wolf", "Bullfrog" })
            {
                CardInfo probe = CardLoader.GetCardByName(name);
                if (probe?.portraitTex == null) continue;
                _referenceSize = probe.portraitTex.bounds.size;
                Trace.Info($"[art] portrait slot measured from {name}: {_referenceSize.x:0.00}x{_referenceSize.y:0.00}");
                return;
            }
        }

        private static Sprite ScaleToPortraitSlot(Sprite source)
        {
            if (Rescaled.TryGetValue(source, out Sprite cached)) return cached;

            // Until a real 3D portrait has been seen there is nothing to scale against.
            // Returning the source unchanged was wrong: a pixel sprite at its own
            // pixels-per-unit is far larger than a card, so it spilled over the frame and
            // buried the cost row. Borrow a reference from a card known to have one.
            if (_referenceSize == Vector2.zero) LearnReferenceSize();
            if (_referenceSize == Vector2.zero)
            {
                Trace.Warn($"[art] no reference portrait yet - leaving '{source.name}' blank");
                return null;
            }

            try
            {
                Rect rect = source.rect;
                // Fit the whole image: scale by whichever axis needs shrinking most.
                float ppu = Mathf.Max(rect.width / _referenceSize.x, rect.height / _referenceSize.y);
                if (ppu <= 0f) return source;

                Sprite scaled = Sprite.Create(source.texture, rect, new Vector2(0.5f, 0.5f), ppu);
                scaled.name = source.name + "_mp_scaled";
                Rescaled[source] = scaled;
                Trace.Info($"[art] scaled '{source.name}' {rect.width}x{rect.height} -> " +
                           $"{scaled.bounds.size.x:0.00}x{scaled.bounds.size.y:0.00} (slot {_referenceSize.x:0.00}x{_referenceSize.y:0.00})");
                return scaled;
            }
            catch (Exception e)
            {
                Trace.Warn($"[art] could not rescale '{source.name}': {e.Message}");
                return source;
            }
        }

        /// <summary>Textures baked from pixel sigils, keyed by the sprite they came from.</summary>
        private static readonly Dictionary<Sprite, Texture> BakedIcons = new Dictionary<Sprite, Texture>();

        /// <summary>Gives Act 2 sigils an icon on the 3D card table.</summary>
        [HarmonyPatch(typeof(AbilityIconInteractable), "LoadIcon")]
        [HarmonyPostfix]
        private static void SubstitutePixelSigil(AbilityInfo ability, ref Texture __result)
        {
            if (__result != null || ability == null) return;
            if (!VersusMode.VersusContext) return;
            if (ability.pixelIcon == null) return;

            Texture baked = BakeSprite(ability.pixelIcon);
            if (baked != null) __result = baked;
        }

        /// <summary>Copies one sprite out of its atlas into a standalone texture.</summary>
        private static Texture BakeSprite(Sprite source)
        {
            if (BakedIcons.TryGetValue(source, out Texture cached)) return cached;

            RenderTexture rt = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                Rect rect = source.rect;
                int w = Mathf.Max(1, (int)rect.width);
                int h = Mathf.Max(1, (int)rect.height);

                rt = RenderTexture.GetTemporary(w, h, 0);
                Graphics.Blit(source.texture, rt);
                RenderTexture.active = rt;

                // The blit stretched the whole atlas over the target, so read back the
                // fraction of it this sprite occupies.
                Texture2D atlas = source.texture;
                var read = new Rect(
                    rect.x / atlas.width * w,
                    rect.y / atlas.height * h,
                    rect.width / atlas.width * w,
                    rect.height / atlas.height * h);

                var baked = new Texture2D(Mathf.Max(1, (int)read.width), Mathf.Max(1, (int)read.height),
                                          TextureFormat.RGBA32, mipChain: false);
                baked.ReadPixels(read, 0, 0);
                baked.Apply();
                baked.filterMode = FilterMode.Point;   // keep it crisp; it's pixel art
                baked.name = source.name + "_mp_baked";

                BakedIcons[source] = baked;
                return baked;
            }
            catch (Exception e)
            {
                Trace.Warn($"[art] could not bake sigil '{source.name}': {e.Message}");
                BakedIcons[source] = null;   // don't retry every frame
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        [HarmonyPatch(typeof(CardDisplayer), nameof(CardDisplayer.GetCostSpriteForCard))]
        [HarmonyPrefix]
        private static bool ClampCostSprite(CardDisplayer __instance, CardInfo card, ref Sprite __result)
        {
            if (card == null) return true;

            if (card.BonesCost > 0)
            {
                var bones = AccessTools.Field(typeof(CardDisplayer), "boneCostTextures")
                                       ?.GetValue(__instance) as Sprite[];
                if (bones == null || bones.Length == 0) return true;
                if (card.BonesCost - 1 < bones.Length) return true;   // in range, let it run

                __result = bones[bones.Length - 1];
                return false;
            }

            if (card.GemsCost != null && card.GemsCost.Count > 0) return true;   // guarded upstream
            if (card.EnergyCost > 0) return true;                                 // guarded upstream

            var blood = AccessTools.Field(typeof(CardDisplayer), "costTextures")
                                   ?.GetValue(__instance) as Sprite[];
            if (blood == null || blood.Length == 0) return true;
            if (card.BloodCost < blood.Length) return true;

            __result = blood[blood.Length - 1];
            return false;
        }
    }
}
