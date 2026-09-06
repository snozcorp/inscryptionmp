using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// Builds the multiplayer menu card's face from one the game already has.
    /// </summary>
    internal static class VersusCardArt
    {
        /// <summary>Colour the source art is multiplied by. Dark enough to read as its own card.</summary>
        private static readonly Color Blood = new Color(0.62f, 0.13f, 0.11f);

        /// <summary>The stamped letters, kept pale so they hold up against the red.</summary>
        private static readonly Color Chalk = new Color(0.94f, 0.90f, 0.82f);

        /// <summary>Flat fill for the middle, once the borrowed illustration is cleared.</summary>
        private static readonly Color Face = new Color(0.30f, 0.055f, 0.05f);

        private static Sprite _cached;

        public static Sprite Build(Sprite source)
        {
            if (_cached != null) return _cached;
            if (source == null) return null;

            try
            {
                Texture2D tex = CopyReadable(source);
                if (tex == null) return null;

                Tint(tex, Blood);
                ClearInterior(tex);
                StampVersus(tex);
                tex.Apply();

                _cached = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    source.pixelsPerUnit);
                _cached.name = "menucard_multiplayer";

                Trace.Info($"[menucard] built a {tex.width}x{tex.height} VS card from '{source.name}'");
                return _cached;
            }
            catch (System.Exception e)
            {
                Trace.Warn($"[menucard] could not build the VS card: {e.Message}");
                return null;
            }
        }

        /// <summary>Copies a sprite out of its atlas into a texture we can write to.</summary>
        private static Texture2D CopyReadable(Sprite source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture rt = null;

            try
            {
                Texture2D atlas = source.texture;
                rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0);
                Graphics.Blit(atlas, rt);
                RenderTexture.active = rt;

                Rect r = source.rect;
                var copy = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(r.x, atlas.height - r.y - r.height, r.width, r.height), 0, 0);
                copy.filterMode = source.texture.filterMode;
                copy.wrapMode = TextureWrapMode.Clamp;
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>Multiplies the art through, so the paper texture survives the recolour.</summary>
        private static void Tint(Texture2D tex, Color tint)
        {
            Color[] px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                px[i].r *= tint.r;
                px[i].g *= tint.g;
                px[i].b *= tint.b;
            }
            tex.SetPixels(px);
        }

        /// <summary>Wipes the borrowed illustration, keeping the card's frame.</summary>
        private static void ClearInterior(Texture2D tex)
        {
            int inset = Mathf.Max(2, tex.width / 12);

            for (int y = inset; y < tex.height - inset; y++)
                for (int x = inset; x < tex.width - inset; x++)
                    tex.SetPixel(x, y, Face);
        }

        // Block letters, one row per line. Wide enough to read once scaled up.
        private static readonly string[] V =
        {
            "X...X",
            "X...X",
            "X...X",
            "X...X",
            ".X.X.",
            ".X.X.",
            "..X..",
        };

        private static readonly string[] S =
        {
            ".XXX.",
            "X...X",
            "X....",
            ".XXX.",
            "....X",
            "X...X",
            ".XXX.",
        };

        /// <summary>Stamps V above S in the middle of the card.</summary>
        private static void StampVersus(Texture2D tex)
        {
            // Sized off the card so it scales with whatever art we were handed.
            int scale = Mathf.Max(2, tex.width / 22);
            int glyphW = 5 * scale;
            int glyphH = 7 * scale;
            int gap = scale * 2;

            int totalH = glyphH * 2 + gap;
            int x = (tex.width - glyphW) / 2;
            int yTop = (tex.height + totalH) / 2 - glyphH;

            DrawGlyph(tex, V, x, yTop, scale);
            DrawGlyph(tex, S, x, yTop - glyphH - gap, scale);
        }

        private static void DrawGlyph(Texture2D tex, string[] rows, int originX, int originY, int scale)
        {
            for (int row = 0; row < rows.Length; row++)
            {
                string line = rows[row];
                for (int col = 0; col < line.Length; col++)
                {
                    if (line[col] != 'X') continue;

                    // Rows read top-down; texture coordinates run bottom-up.
                    int px = originX + col * scale;
                    int py = originY + (rows.Length - 1 - row) * scale;
                    FillBlock(tex, px, py, scale);
                }
            }
        }

        private static void FillBlock(Texture2D tex, int x, int y, int size)
        {
            for (int dy = 0; dy < size; dy++)
            {
                int py = y + dy;
                if (py < 0 || py >= tex.height) continue;

                for (int dx = 0; dx < size; dx++)
                {
                    int px = x + dx;
                    if (px < 0 || px >= tex.width) continue;
                    tex.SetPixel(px, py, Chalk);
                }
            }
        }
    }
}
