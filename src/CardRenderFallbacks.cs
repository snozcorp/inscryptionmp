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
                CardInfo probe = Cards.ByName(name);
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

            // The Act 1 card renderer has no sprites assigned for the two currencies added
            // by later acts. CardDisplayer silently falls through to blood cost zero, which
            // leaves almost every Energy and Mox card with a blank corner. Draw compact
            // paper-card versions that fit the exact same slot as Act 1's own cost art.
            var blood = AccessTools.Field(typeof(CardDisplayer), "costTextures")
                                   ?.GetValue(__instance) as Sprite[];
            Sprite costTemplate = blood != null && blood.Length > 0 ? blood[0] : null;

            if (card.BonesCost > 0)
            {
                var bones = AccessTools.Field(typeof(CardDisplayer), "boneCostTextures")
                                       ?.GetValue(__instance) as Sprite[];
                if (bones == null || bones.Length == 0) return true;
                if (card.BonesCost - 1 < bones.Length) return true;   // in range, let it run

                __result = bones[bones.Length - 1];
                return false;
            }

            if (card.GemsCost != null && card.GemsCost.Count > 0)
            {
                __result = CrossActCostSprite.ForGems(card.GemsCost, costTemplate);
                return __result == null;
            }

            if (card.EnergyCost > 0)
            {
                __result = CrossActCostSprite.ForEnergy(card.EnergyCost, costTemplate);
                return __result == null;
            }

            if (blood == null || blood.Length == 0) return true;
            if (card.BloodCost < blood.Length) return true;

            __result = blood[blood.Length - 1];
            return false;
        }

        /// <summary>Act 1-style fallback artwork for costs its prefab predates.</summary>
        private static class CrossActCostSprite
        {
            private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

            // Tiny block numerals stay sharp after CardRenderCamera downsamples the face.
            private static readonly string[][] Digits =
            {
                new[] { "111", "101", "101", "101", "111" },
                new[] { "010", "110", "010", "010", "111" },
                new[] { "110", "001", "111", "100", "111" },
                new[] { "110", "001", "111", "001", "110" },
                new[] { "101", "101", "111", "001", "001" },
                new[] { "111", "100", "110", "001", "110" },
                new[] { "011", "100", "111", "101", "111" },
                new[] { "111", "001", "010", "010", "010" },
                new[] { "111", "101", "111", "101", "111" },
                new[] { "111", "101", "111", "001", "110" },
            };

            public static Sprite ForEnergy(int cost, Sprite template)
            {
                string key = "energy:" + cost;
                if (Cache.TryGetValue(key, out Sprite cached)) return cached;

                Texture2D tex = Blank(template, out float ppu, out Vector2 pivot);
                if (tex == null) return null;

                var ink = new Color32(24, 65, 82, 255);
                int unit = Mathf.Max(1, Mathf.Min(tex.width, tex.height) / 18);
                int bodyW = unit * 11;
                int bodyH = unit * 13;
                int left = (tex.width - bodyW) / 2;
                int bottom = (tex.height - bodyH) / 2;

                // Battery outline and cap, with the amount printed inside.
                Line(tex, left, bottom, left + bodyW, bottom, unit, ink);
                Line(tex, left, bottom + bodyH, left + bodyW, bottom + bodyH, unit, ink);
                Line(tex, left, bottom, left, bottom + bodyH, unit, ink);
                Line(tex, left + bodyW, bottom, left + bodyW, bottom + bodyH, unit, ink);
                Fill(tex, left + bodyW / 3, bottom + bodyH, bodyW / 3, unit * 2, ink);
                DrawNumber(tex, Mathf.Clamp(cost, 0, 99), left + bodyW / 2, bottom + bodyH / 2, unit, ink);

                return Store(key, tex, ppu, pivot);
            }

            public static Sprite ForGems(List<GemType> gems, Sprite template)
            {
                string key = "gems:" + string.Join("-", gems);
                if (Cache.TryGetValue(key, out Sprite cached)) return cached;

                Texture2D tex = Blank(template, out float ppu, out Vector2 pivot);
                if (tex == null) return null;

                int count = Mathf.Max(1, gems.Count);
                int cellW = tex.width / count;
                int radius = Mathf.Max(3, Mathf.Min(cellW, tex.height) / 3);
                int thickness = Mathf.Max(1, radius / 5);
                int cy = tex.height / 2;

                for (int i = 0; i < count; i++)
                {
                    int cx = cellW * i + cellW / 2;
                    Color32 color = GemColor(gems[i]);
                    switch (gems[i])
                    {
                        case GemType.Green: // triangle
                            Line(tex, cx, cy + radius, cx - radius, cy - radius, thickness, color);
                            Line(tex, cx - radius, cy - radius, cx + radius, cy - radius, thickness, color);
                            Line(tex, cx + radius, cy - radius, cx, cy + radius, thickness, color);
                            break;
                        case GemType.Blue: // diamond
                            Line(tex, cx, cy + radius, cx - radius, cy, thickness, color);
                            Line(tex, cx - radius, cy, cx, cy - radius, thickness, color);
                            Line(tex, cx, cy - radius, cx + radius, cy, thickness, color);
                            Line(tex, cx + radius, cy, cx, cy + radius, thickness, color);
                            break;
                        default: // orange circle
                            Circle(tex, cx, cy, radius, thickness, color);
                            break;
                    }
                }

                return Store(key, tex, ppu, pivot);
            }

            private static Texture2D Blank(Sprite template, out float ppu, out Vector2 pivot)
            {
                int width = template == null ? 64 : Mathf.Max(16, (int)template.rect.width);
                int height = template == null ? 64 : Mathf.Max(16, (int)template.rect.height);
                ppu = template == null ? 100f : template.pixelsPerUnit;
                pivot = template == null
                    ? new Vector2(0.5f, 0.5f)
                    : new Vector2(template.pivot.x / template.rect.width, template.pivot.y / template.rect.height);

                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.name = "mp_cross_act_cost";
                tex.filterMode = FilterMode.Point;
                var clear = new Color32[width * height];
                tex.SetPixels32(clear);
                return tex;
            }

            private static Sprite Store(string key, Texture2D tex, float ppu, Vector2 pivot)
            {
                tex.Apply();
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu);
                sprite.name = "mp_" + key.Replace(':', '_');
                Cache[key] = sprite;
                return sprite;
            }

            private static Color32 GemColor(GemType gem)
            {
                switch (gem)
                {
                    case GemType.Green:  return new Color32(43, 128, 58, 255);
                    case GemType.Blue:   return new Color32(31, 89, 142, 255);
                    default:             return new Color32(185, 82, 28, 255);
                }
            }

            private static void DrawNumber(Texture2D tex, int value, int cx, int cy, int scale, Color32 color)
            {
                string text = value.ToString();
                int glyphW = 3 * scale;
                int gap = scale;
                int totalW = text.Length * glyphW + (text.Length - 1) * gap;
                int x0 = cx - totalW / 2;
                int y0 = cy - (5 * scale) / 2;

                for (int n = 0; n < text.Length; n++)
                {
                    string[] rows = Digits[text[n] - '0'];
                    for (int row = 0; row < rows.Length; row++)
                        for (int col = 0; col < rows[row].Length; col++)
                            if (rows[row][col] == '1')
                                Fill(tex, x0 + n * (glyphW + gap) + col * scale,
                                     y0 + (4 - row) * scale, scale, scale, color);
                }
            }

            private static void Circle(Texture2D tex, int cx, int cy, int radius, int thickness, Color32 color)
            {
                int inner = Mathf.Max(0, radius - thickness);
                int r2 = radius * radius;
                int i2 = inner * inner;
                for (int y = -radius; y <= radius; y++)
                    for (int x = -radius; x <= radius; x++)
                    {
                        int d = x * x + y * y;
                        if (d <= r2 && d >= i2) Pixel(tex, cx + x, cy + y, color);
                    }
            }

            private static void Line(Texture2D tex, int x0, int y0, int x1, int y1, int thickness, Color32 color)
            {
                int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
                int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
                int err = dx + dy;
                while (true)
                {
                    Fill(tex, x0 - thickness / 2, y0 - thickness / 2, thickness, thickness, color);
                    if (x0 == x1 && y0 == y1) break;
                    int e2 = 2 * err;
                    if (e2 >= dy) { err += dy; x0 += sx; }
                    if (e2 <= dx) { err += dx; y0 += sy; }
                }
            }

            private static void Fill(Texture2D tex, int x, int y, int width, int height, Color32 color)
            {
                for (int py = y; py < y + height; py++)
                    for (int px = x; px < x + width; px++) Pixel(tex, px, py, color);
            }

            private static void Pixel(Texture2D tex, int x, int y, Color32 color)
            {
                if (x >= 0 && x < tex.width && y >= 0 && y < tex.height) tex.SetPixel(x, y, color);
            }
        }
    }
}
