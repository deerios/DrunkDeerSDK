#!/usr/bin/env bash
# Build self-contained, single-file `deerkb` binaries for every supported platform.
# Output: deerkb/dist/deerkb-<rid>[.exe]
#
# Usage:  ./publish.sh [version]
#   version  optional; overrides the csproj <Version> (e.g. 0.2.0)
set -euo pipefail

cd "$(dirname "$0")"
PROJ="src/DeerKB.csproj"
OUT="dist"
VERSION="${1:-}"

RIDS=(linux-x64 linux-arm64 win-x64 win-arm64 osx-x64 osx-arm64)

rm -rf "$OUT"
mkdir -p "$OUT"

VERSION_ARG=()
[[ -n "$VERSION" ]] && VERSION_ARG=("-p:Version=$VERSION")

for rid in "${RIDS[@]}"; do
	echo ">> publishing $rid"
	dotnet publish "$PROJ" -c Release -r "$rid" --self-contained true \
		-p:PublishSingleFile=true -p:PublishTrimmed=false \
		"${VERSION_ARG[@]}" \
		-o "$OUT/tmp-$rid" --nologo -v q

	ext=""
	[[ "$rid" == win-* ]] && ext=".exe"
	cp "$OUT/tmp-$rid/deerkb$ext" "$OUT/deerkb-$rid$ext"
	rm -rf "$OUT/tmp-$rid"
done

echo
echo "Artifacts in $OUT/:"
ls -1 "$OUT"

# Also produce the NuGet tool package (for `dotnet tool install -g deerkb`).
echo
echo ">> packing dotnet tool"
dotnet pack "$PROJ" -c Release "${VERSION_ARG[@]}" -o "$OUT" --nologo -v q
ls -1 "$OUT"/*.nupkg
