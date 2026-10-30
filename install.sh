#!/bin/sh
# claude-usage-bar installer for macOS and Linux (and Git Bash on Windows)
#   curl -fsSL https://raw.githubusercontent.com/murilo999-666/claude-usage-bar/main/install.sh | sh
#   curl -fsSL .../install.sh | sh -s -- --lang en        (options are passed to "install")
#
# Downloads the latest release, checks its SHA-256 against checksums.txt and runs "install".
set -eu

repo="murilo999-666/claude-usage-bar"
base="https://github.com/$repo/releases/latest/download"

case "$(uname -s)" in
  Darwin) os=darwin ;;
  Linux) os=linux ;;
  MINGW* | MSYS* | CYGWIN*) os=windows ;;
  *) echo "claude-usage-bar: unsupported system: $(uname -s)" >&2; exit 1 ;;
esac
case "$(uname -m)" in
  x86_64 | amd64) arch=amd64 ;;
  arm64 | aarch64) arch=arm64 ;;
  *) echo "claude-usage-bar: unsupported CPU: $(uname -m)" >&2; exit 1 ;;
esac
asset="claude-usage-bar-$os-$arch"
[ "$os" = windows ] && asset="$asset.exe"

tmp=$(mktemp -d 2>/dev/null || mktemp -d -t claude-usage-bar)
trap 'rm -rf "$tmp"' EXIT INT TERM

download() {
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL -o "$2" "$1"
  elif command -v wget >/dev/null 2>&1; then
    wget -q -O "$2" "$1"
  else
    echo "claude-usage-bar: curl or wget is needed" >&2
    exit 1
  fi
}

echo "Downloading $asset ..."
download "$base/$asset" "$tmp/$asset"
download "$base/checksums.txt" "$tmp/checksums.txt"

want=$(awk -v a="$asset" '$2 == a || $2 == "*" a { print $1 }' "$tmp/checksums.txt")
if command -v sha256sum >/dev/null 2>&1; then
  got=$(sha256sum "$tmp/$asset" | awk '{ print $1 }')
else
  got=$(shasum -a 256 "$tmp/$asset" | awk '{ print $1 }')
fi
if [ -z "$want" ] || [ "$want" != "$got" ]; then
  echo "claude-usage-bar: checksum mismatch for $asset. Nothing was installed." >&2
  exit 1
fi

chmod +x "$tmp/$asset"
"$tmp/$asset" install "$@"
