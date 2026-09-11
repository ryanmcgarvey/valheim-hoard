#!/usr/bin/env bash
# Cut a release: set the version everywhere, build + check, package a Thunderstore-layout
# zip into dist/, commit + tag, push, and create the GitHub release with the zip attached.
#
#   ./release.sh 0.2.0            full release
#   ./release.sh 0.2.0 --package  build + zip only (no git, no GitHub)
set -euo pipefail
cd "$(dirname "$0")"
ver="${1:?usage: release.sh <version> [--package]}"
[[ "$ver" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "version must be x.y.z" >&2; exit 2; }
package_only=0; [[ "${2:-}" == "--package" ]] && package_only=1

# version lives in three places; keep them in step
sed -i -E "s|(<Version>)[^<]+(</Version>)|\1$ver\2|" Hoard.csproj
sed -i -E "s|(public const string Version = \")[^\"]+(\";)|\1$ver\2|" src/Plugin.cs
sed -i -E "s|(\"version_number\": \")[^\"]+(\")|\1$ver\2|" package/manifest.json

./build.sh --no-install

rm -rf dist && mkdir -p dist/stage/plugins
cp bin/Release/Hoard.dll dist/stage/plugins/
cp package/manifest.json package/icon.png README.md CHANGELOG.md dist/stage/
zip="dist/Hoard-$ver.zip"
(cd dist/stage && zip -qr "../Hoard-$ver.zip" .)
rm -rf dist/stage
echo "packaged $zip"; unzip -l "$zip"
(( package_only )) && exit 0

grep -q "## $ver" CHANGELOG.md || { echo "CHANGELOG.md has no '## $ver' section" >&2; exit 1; }
git add -A
git commit -m "Release $ver" || true
git tag -a "v$ver" -m "Hoard $ver"
git push && git push --tags
notes="$(awk -v v="## $ver" '$0 ~ "^## " {p = index($0, v) == 1} p' CHANGELOG.md | tail -n +2)"
gh release create "v$ver" "$zip" --title "Hoard $ver" --notes "$notes"
