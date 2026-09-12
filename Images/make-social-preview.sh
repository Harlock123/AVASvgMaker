#!/usr/bin/env bash
#
# Rebuilds Images/social-preview.png - the card GitHub shows when a link to the repository is
# unfurled somewhere else. 1280x640 is what GitHub asks for and what it displays.
#
# GitHub has no API for this: the finished file has to be uploaded by hand, under
# Settings -> General -> Social preview. So the card is generated here and committed, and
# uploading it is a separate manual step whenever it changes.
#
# The right-hand panel is cropped out of Images/overview.png, so reshoot that first if the
# app's look changes. Needs ImageMagick.

set -euo pipefail
S="$(cd "$(dirname "$0")" && pwd)"
SHOT="$S/shot.png"

# The flowchart out of the middle of the overview screenshot, at the panel's shape.
magick "$S/overview.png" -crop 1170x1130+700+345 +repage \
  -resize 700x640^ -gravity center -extent 700x640 "$SHOT"

trap 'rm -f "$SHOT"' EXIT

magick -size 1280x640 xc:'#0A0E18' \
  "$SHOT" -geometry +580+0 -composite \
  -fill '#0078D7' -draw "rectangle 580,0 583,640" \
  -font Adwaita-Sans-Bold -pointsize 68 -fill '#FFFFFF' -annotate +72+180 'AVASvgMaker' \
  -font Adwaita-Sans -pointsize 30 -fill '#9BB0CC' -annotate +72+234 'A Visio-style diagram editor' \
  -fill '#0078D7' -draw "rectangle 72,272 152,276" \
  -font Adwaita-Sans -pointsize 21 -fill '#7B8FAC' \
    -annotate +72+332 'Reads Visio .vsdx' \
    -annotate +72+366 'Exports SVG, PNG, PDF and Mermaid' \
    -annotate +72+400 '96 stencils, self-routing connectors, swimlanes' \
  -font Adwaita-Sans-Bold -pointsize 20 -fill '#0078D7' \
    -annotate +72+552 'github.com/Harlock123/AVASvgMaker' \
  -font Adwaita-Sans -pointsize 19 -fill '#5F7186' \
    -annotate +72+588 'Avalonia · .NET 9 · Windows, macOS, Linux' \
  -depth 8 -strip "PNG24:$S/social-preview.png"
