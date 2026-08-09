#!/usr/bin/env bash
set -euo pipefail

# Finds .mp3 sample files that aren't actually MPEG audio (e.g. an AAC-in-MP4
# file saved with a .mp3 extension, like 8_uur.mp3 - MP3Sharp can't decode
# these at all, so the bot joins voice and plays silence). Each such file is
# backed up next to itself under an "_originals" subfolder, then re-encoded
# in place to real 48kHz stereo MP3 via ffmpeg/libmp3lame.
#
# Usage: ./reencode-mislabeled-samples.sh [samples-dir]
#   samples-dir defaults to volume/samples (relative to cwd)

SAMPLES_DIR="${1:-volume/samples}"

if ! command -v ffmpeg >/dev/null 2>&1; then
    echo "ffmpeg is required to run this script (not found in PATH)." >&2
    exit 1
fi
if ! command -v ffprobe >/dev/null 2>&1; then
    echo "ffprobe is required to run this script (not found in PATH)." >&2
    exit 1
fi
if [ ! -d "$SAMPLES_DIR" ]; then
    echo "Samples directory not found: $SAMPLES_DIR" >&2
    exit 1
fi

fixed=0
skipped=0
failed=0

while IFS= read -r -d '' file; do
    dir="$(dirname "$file")"
    base="$(basename "$file")"

    codec="$(ffprobe -v error -select_streams a:0 -show_entries stream=codec_name -of csv=p=0 "$file" 2>/dev/null || true)"

    if [ "$codec" = "mp3" ]; then
        continue # already real MPEG audio, nothing to do
    fi

    if [ -z "$codec" ]; then
        echo "WARN: could not probe an audio stream in '$file', skipping" >&2
        skipped=$((skipped + 1))
        continue
    fi

    echo "Re-encoding '$file' (detected codec: $codec)..."

    backup_dir="$dir/_originals"
    mkdir -p "$backup_dir"
    if [ ! -e "$backup_dir/$base" ]; then
        cp "$file" "$backup_dir/$base"
    fi

    tmp_out="${file}.reencode.tmp"
    if ffmpeg -y -v error -i "$backup_dir/$base" -vn -ac 2 -ar 48000 -codec:a libmp3lame -q:a 2 -f mp3 "$tmp_out"; then
        mv "$tmp_out" "$file"
        echo "  done."
        fixed=$((fixed + 1))
    else
        echo "  ffmpeg failed on '$file' (original preserved in '$backup_dir/$base')" >&2
        rm -f "$tmp_out"
        failed=$((failed + 1))
    fi
done < <(find "$SAMPLES_DIR" -type d -name "_originals" -prune -o -type f -iname "*.mp3" -print0)

echo ""
echo "Done. Re-encoded: $fixed, skipped: $skipped, failed: $failed"
