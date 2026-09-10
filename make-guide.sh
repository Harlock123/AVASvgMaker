#!/usr/bin/env bash
#
# Rebuilds USERGUIDE.pdf from USERGUIDE.md.
#
# USERGUIDE.md is the thing to edit; the PDF is only ever generated from it. Both are kept in
# the repository, so someone who wants the guide does not have to build it, and someone who
# changes the app has one file to update.
#
# Needs nothing but the .NET SDK.

set -euo pipefail
cd "$(dirname "$0")"

VERSION="${1:-$(git describe --tags --abbrev=0 2>/dev/null | sed 's/^v//' || echo '')}"

dotnet run --project Tools/GuideBuilder -v q -- USERGUIDE.md USERGUIDE.pdf "$VERSION"
