using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using DiskCardGame;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// Developer utility that writes the Act 2 and Act 3 deck pools exactly as the mod
    /// renders them on Act 1's card stock. F6 exports interactively; the command-line form
    /// is used to build a complete, repeatable share package.
    /// </summary>
    internal sealed class CardArtExporter : MonoBehaviour
    {
        private const string Command = "--mp-export-cards";
        private const string DevVariable = "INSCRYPTIONMP_DEV";

        /// <summary>Only for a developer: the export flag on the command line, or the dev variable set to 1.</summary>
        internal static bool Wanted()
        {
            if (Environment.GetEnvironmentVariable(DevVariable) == "1") return true;
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith(Command, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private bool _running;
        private bool _automatic;
        private string _requestedDirectory;

        private void Awake()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg.Equals(Command, StringComparison.OrdinalIgnoreCase))
                {
                    _automatic = true;
                    _requestedDirectory = DefaultDirectory();
                    break;
                }

                string prefix = Command + "=";
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    _automatic = true;
                    _requestedDirectory = arg.Substring(prefix.Length).Trim('"');
                    break;
                }
            }

            if (_automatic) StartCoroutine(BeginWhenReady());
        }

        private void Update()
        {
            if (!_running && Input.GetKeyDown(KeyCode.F6))
            {
                _requestedDirectory = DefaultDirectory();
                StartCoroutine(BeginWhenReady());
            }
        }

        private static string DefaultDirectory() =>
            Path.Combine(Paths.GameRootPath, "BepInEx", "card-export");

        private IEnumerator BeginWhenReady()
        {
            if (_running) yield break;
            _running = true;

            Trace.Info("[export] card export requested");
            Notice.Busy("Preparing Act 1 card renderer...");

            if (Singleton<CardSpawner>.Instance == null || Singleton<CardRenderCamera>.Instance == null)
                VersusMode.LoadTableOnly();

            float deadline = Time.realtimeSinceStartup + 45f;
            while ((Singleton<CardSpawner>.Instance == null || Singleton<CardRenderCamera>.Instance == null)
                   && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (Singleton<CardSpawner>.Instance == null || Singleton<CardRenderCamera>.Instance == null)
            {
                Fail("Act 1 card renderer did not become available within 45 seconds.");
                yield break;
            }

            yield return ExportAll(_requestedDirectory);

            if (_automatic)
            {
                if (VersusMode.LoadedTableForDeck) VersusMode.LeaveDeckTable();
                yield return new WaitForSecondsRealtime(2f);
                Application.Quit();
            }
        }

        private IEnumerator ExportAll(string root)
        {
            int exported = 0;
            int failed = 0;
            MatchAct previousAct = ActInfo.Selected;
            var manifest = new StringBuilder("act,id,display_name,file,width,height\r\n");

            try
            {
                Directory.CreateDirectory(root);
                foreach (string old in Directory.GetFiles(root, "*.png", SearchOption.AllDirectories))
                    File.Delete(old);

                foreach (MatchAct act in new[] { MatchAct.Act2, MatchAct.Act3 })
                {
                    ActInfo.Selected = act;
                    string actFolder = Path.Combine(root, act == MatchAct.Act2 ? "Act 2" : "Act 3");
                    Directory.CreateDirectory(actFolder);
                    List<CardInfo> cards = PoolFor(act);
                    var duplicateDisplayNames = new HashSet<string>(
                        cards.GroupBy(c => c.DisplayedNameEnglish ?? "", StringComparer.OrdinalIgnoreCase)
                             .Where(g => g.Count() > 1)
                             .Select(g => g.Key),
                        StringComparer.OrdinalIgnoreCase);
                    Trace.Info($"[export] {ActInfo.Name(act)}: rendering {cards.Count} cards");

                    foreach (CardInfo info in cards)
                    {
                        PlayableCard card = null;
                        try
                        {
                            card = CardSpawner.SpawnPlayableCard(info.Clone() as CardInfo);
                        }
                        catch (Exception e)
                        {
                            failed++;
                            Trace.Warn($"[export] {ActInfo.Name(act)} '{info?.name}' could not spawn: {e.Message}");
                        }

                        if (card == null) continue;

                        // CardRenderCamera serialises requests. Wait for this card's copied
                        // face to replace the prefab's stock texture; a fixed frame count
                        // races the first render while the scene is still warming up.
                        Texture initialFace = card.StatsLayer.GetTexture(front: true);
                        Texture renderedFace = initialFace;
                        int frames = 0;
                        while (ReferenceEquals(renderedFace, initialFace) && frames++ < 120)
                        {
                            yield return new WaitForEndOfFrame();
                            renderedFace = card.StatsLayer.GetTexture(front: true);
                        }

                        try
                        {
                            if (ReferenceEquals(renderedFace, initialFace))
                                throw new TimeoutException("renderer did not assign a new face texture within 120 frames");

                            Texture2D copy = ReadableCopy(renderedFace);
                            if (copy == null) throw new InvalidOperationException("renderer returned no face texture");

                            string label = info.DisplayedNameEnglish;
                            if (duplicateDisplayNames.Contains(label ?? "")) label += " [" + info.name + "]";
                            string fileName = SafeFileName(label, info.name) + ".png";
                            string path = Path.Combine(actFolder, fileName);
                            File.WriteAllBytes(path, copy.EncodeToPNG());
                            manifest.Append(Csv(ActInfo.Name(act))).Append(',')
                                    .Append(Csv(info.name)).Append(',')
                                    .Append(Csv(info.DisplayedNameEnglish)).Append(',')
                                    .Append(Csv((act == MatchAct.Act2 ? "Act 2/" : "Act 3/") + fileName)).Append(',')
                                    .Append(copy.width).Append(',').Append(copy.height).Append("\r\n");
                            Destroy(copy);
                            exported++;
                        }
                        catch (Exception e)
                        {
                            failed++;
                            Trace.Warn($"[export] {ActInfo.Name(act)} '{info?.name}' failed: {e.Message}");
                        }

                        Destroy(card.gameObject);
                    }
                }

                File.WriteAllText(Path.Combine(root, "manifest.csv"), manifest.ToString(), Encoding.UTF8);
                File.WriteAllText(Path.Combine(root, "README.txt"),
                    "Inscryption Online - Act 2 and Act 3 cards on Act 1 card stock\r\n\r\n" +
                    "These PNG files are exported from the game's own runtime card renderer.\r\n" +
                    "Act 2 pixel portraits are fitted into the Act 1 portrait frame by the mod.\r\n" +
                    "Act 3 portraits are rendered on the same Act 1 card stock.\r\n\r\n" +
                    "Energy costs use a numbered battery mark. Mox costs use the familiar orange circle, green triangle, and blue diamond.\r\n\r\n" +
                    "The folders contain the cards available in Inscryption Online's Act 2 and Act 3 deck builders.\r\n" +
                    "For personal, non-commercial fan use only. Inscryption and its artwork belong to Daniel Mullins Games.\r\n",
                    Encoding.UTF8);
                File.WriteAllText(Path.Combine(root, "export-complete.txt"),
                    $"Exported: {exported}\r\nFailed: {failed}\r\nGenerated: {DateTime.UtcNow:O}\r\n",
                    Encoding.UTF8);

                Trace.Info($"[export] complete: {exported} cards, {failed} failed -> {root}");
                if (failed == 0) Notice.Good($"Exported {exported} cards.");
                else Notice.Bad($"Exported {exported} cards; {failed} failed. Check mp-trace.log.");
            }
            finally
            {
                ActInfo.Selected = previousAct;
                _running = false;
            }
        }

        /// <summary>Mirrors the exact pool rules used by the multiplayer deck builder.</summary>
        private static List<CardInfo> PoolFor(MatchAct act)
        {
            var offered = new HashSet<CardMetaCategory>(ActInfo.OfferedCategories(act));
            var always = new HashSet<string>(ActInfo.AlwaysOffered(act));
            var all = ScriptableObjectLoader<CardInfo>.AllData ?? new List<CardInfo>();

            return all.Where(c => c != null
                                  && DeckStore.CanUseInDeck(c, act)
                                  && (always.Contains(c.name)
                                      || (c.metaCategories != null && c.metaCategories.Any(offered.Contains))))
                      .OrderBy(c => c.DisplayedNameEnglish)
                      .ThenBy(c => c.name)
                      .ToList();
        }

        private static Texture2D ReadableCopy(Texture source)
        {
            if (source == null) return null;

            RenderTexture previous = RenderTexture.active;
            RenderTexture rt = null;
            try
            {
                rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                copy.Apply();
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static string SafeFileName(string displayName, string id)
        {
            string value = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            value = value.Trim().TrimEnd('.');
            return string.IsNullOrEmpty(value) ? id : value;
        }

        private static string Csv(string value) =>
            "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

        private void Fail(string message)
        {
            _running = false;
            Trace.Error("[export] " + message);
            Notice.Bad("Card export failed. Check mp-trace.log.");
            try
            {
                Directory.CreateDirectory(_requestedDirectory);
                File.WriteAllText(Path.Combine(_requestedDirectory, "export-failed.txt"), message);
            }
            catch { }
            if (_automatic) Application.Quit();
        }
    }
}
