"""Zip a directory's contents, for the release archives.

Windows has no `zip`, and PowerShell's Compress-Archive has written backslashes
into entry names, which some extractors then treat as part of the filename rather
than as a path. zipfile writes forward slashes, so it is the safe way to do this.
"""
import os
import sys
import zipfile

source, target = sys.argv[1], sys.argv[2]

if os.path.exists(target):
    os.remove(target)

with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED) as z:
    for root, dirs, files in os.walk(source):
        dirs.sort()
        for name in sorted(files):
            full = os.path.join(root, name)
            entry = os.path.relpath(full, source).replace(os.sep, '/')
            z.write(full, entry)

print("    %s (%.0f KB)" % (target, os.path.getsize(target) / 1024.0))
