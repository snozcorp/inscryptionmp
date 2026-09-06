#!/usr/bin/env bash
# Build the release archives for a version.
#
#   tools/package.sh            package the version in src/Plugin.cs
#   tools/package.sh 1.3.0      package that version, checking Plugin.cs agrees
#
# Produces three files in dist/:
#   InscryptionOnline-<v>.zip               the mod on its own
#   InscryptionOnline-<v>-with-BepInEx.zip  the same, plus a BepInEx that works
#   InscryptionOnline-thunderstore-<v>.zip  Thunderstore's layout, with a manifest
#
# The BepInEx payload is lifted from the previous with-BepInEx archive rather than kept
# in the repo: it is 1.8 MB of somebody else's binaries and has not changed since 1.0.0.
# The Thunderstore icon is ours, so that lives in tools/.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

VERSION="$(sed -n 's/.*public const string Version = "\(.*\)".*/\1/p' src/Plugin.cs)"
[ -n "$VERSION" ] || { echo "could not read the version out of src/Plugin.cs"; exit 1; }

if [ -n "${1:-}" ] && [ "$1" != "$VERSION" ]; then
  echo "asked for $1 but src/Plugin.cs says $VERSION - bump it first"; exit 1
fi

CSPROJ_VERSION="$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' src/InscryptionMP.csproj)"
if [ "$CSPROJ_VERSION" != "$VERSION" ]; then
  echo "src/Plugin.cs says $VERSION but the csproj says $CSPROJ_VERSION"; exit 1
fi

echo "==> packaging $VERSION"

echo "==> building"
dotnet build src/InscryptionMP.csproj -c Release -v q --nologo | tail -3
DLL="src/bin/Release/net472/InscryptionMP.dll"
[ -f "$DLL" ] || { echo "build produced no dll at $DLL"; exit 1; }

STAGE=".tmp/package"
rm -rf "$STAGE"
mkdir -p "$STAGE/plain/BepInEx/plugins" "$STAGE/thunderstore/plugins" dist

cp "$DLL" "$STAGE/plain/BepInEx/plugins/InscryptionMP.dll"
cp "$DLL" "$STAGE/thunderstore/plugins/InscryptionMP.dll"

# ------------------------------------------------------------------ in-zip readme
sed "s/@VERSION@/$VERSION/g" tools/release-readme.txt > "$STAGE/plain/README.txt"

# ------------------------------------------------------------------ thunderstore
sed "s/@VERSION@/$VERSION/g" tools/manifest.json > "$STAGE/thunderstore/manifest.json"
cp README.md "$STAGE/thunderstore/README.md"

cp tools/icon.png "$STAGE/thunderstore/icon.png"

PREVIOUS="$(ls -1 dist/InscryptionOnline-*-with-BepInEx.zip 2>/dev/null | tail -1 || true)"
[ -n "$PREVIOUS" ] || { echo "no previous with-BepInEx archive to take BepInEx from"; exit 1; }

# ------------------------------------------------------------------ with BepInEx
mkdir -p "$STAGE/bundled"
unzip -o -q "$PREVIOUS" -d "$STAGE/bundled" -x 'BepInEx/plugins/*' 'README.txt'
cp -r "$STAGE/plain/." "$STAGE/bundled/"

# ------------------------------------------------------------------ zip them up
zip_dir() {   # zip_dir <source dir> <output>
  python tools/zipdir.py "$1" "$2"
}

zip_dir "$STAGE/plain"       "dist/InscryptionOnline-$VERSION.zip"
zip_dir "$STAGE/bundled"     "dist/InscryptionOnline-$VERSION-with-BepInEx.zip"
zip_dir "$STAGE/thunderstore" "dist/InscryptionOnline-thunderstore-$VERSION.zip"

rm -rf "$STAGE"
echo "done."
