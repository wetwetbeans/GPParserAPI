#!/usr/bin/env python3
"""
Export Guitar Pro (.gp3/.gp4/.gp5/.gpx) to a compact JSON your Unity app can read.

Outputs (per note):
- string (1=high E ... 6=low E)
- fret
- velocity (0-127)
- palmMute (bool)
- start (ticks)
- duration (ticks)

Top-level includes:
- title, artist, tempo (first/global)
- ticksPerBeat: 480   <-- matches your Unity TicksPerBeat
- tracks[...] with strings tuning (MIDI values) if you need it later
"""

import argparse
import json
import os
import sys

import guitarpro  # pip install pyguitarpro

# We’ll normalize everything to this so Unity matches 1:1.
UNITY_TICKS_PER_BEAT = 480

def _safe_velocity(note):
    # pyguitarpro Note.velocity is usually 0..127
    try:
        v = int(getattr(note, "velocity", 100))
        return max(0, min(127, v))
    except Exception:
        return 100

def _safe_palmmute(note):
    try:
        eff = getattr(note, "effect", None)
        return bool(getattr(eff, "palmMute", False))
    except Exception:
        return False

def _tempo_fallback(song):
    # Try to find a sensible tempo if not at song.tempo
    if getattr(song, "tempo", None):
        return float(song.tempo)
    # Fallback to first tempo marker if present
    try:
        for m in song.measures:
            if m.tempo is not None:
                return float(m.tempo.value)
    except Exception:
        pass
    return 120.0

def parse_gp_file(input_path):
    song = guitarpro.parse(input_path)

    export = {
        "title": getattr(song, "title", "") or "",
        "artist": getattr(song, "artist", "") or "",
        "tempo": _tempo_fallback(song),
        "ticksPerBeat": UNITY_TICKS_PER_BEAT,
        "tracks": []
    }

    # Some GP versions use 480 internally already; we keep it 1:1.
    # If you ever discover a different source TPB, add a scale factor here.
    source_tpb = UNITY_TICKS_PER_BEAT
    scale = UNITY_TICKS_PER_BEAT / float(source_tpb)

    for track in song.tracks:
        track_data = {
            "name": track.name,
            # Track strings as MIDI pitches (useful later for tuning/capo handling)
            "strings": [int(s.value) for s in getattr(track, "strings", [])],
            "measures": []
        }

        for measure in track.measures:
            measure_data = {"voices": []}

            for voice in measure.voices:
                voice_data = {"beats": []}

                for beat in voice.beats:
                    # beat.start and beat.duration.time are in ticks relative to GP's TPB.
                    start_ticks = int(round(float(getattr(beat, "start", 0)) * scale))
                    duration_ticks = int(round(float(getattr(beat.duration, "time", 0)) * scale))

                    beat_data = {
                        "start": start_ticks,
                        "duration": duration_ticks,
                        "notes": []
                    }

                    for note in beat.notes or []:
                        beat_data["notes"].append({
                            "string": int(note.string),            # 1..6 (1 = high E)
                            "fret": int(note.value),               # fret number
                            "velocity": _safe_velocity(note),      # 0..127
                            "palmMute": _safe_palmmute(note)       # bool
                        })

                    voice_data["beats"].append(beat_data)

                measure_data["voices"].append(voice_data)

            track_data["measures"].append(measure_data)

        export["tracks"].append(track_data)

    return export


def main():
    p = argparse.ArgumentParser(description="Convert Guitar Pro file to JSON for Unity.")
    p.add_argument("input", help="Path to .gp3/.gp4/.gp5/.gpx file")
    p.add_argument("-o", "--output", help="Output JSON path (default: alongside input)")
    p.add_argument("--pretty", action="store_true", help="Pretty-print JSON")
    args = p.parse_args()

    input_path = args.input
    if not os.path.isfile(input_path):
        print(f"❌ File not found: {input_path}", file=sys.stderr)
        sys.exit(1)

    out_path = args.output
    if not out_path:
        base = os.path.splitext(os.path.basename(input_path))[0]
        out_path = os.path.join(os.path.dirname(input_path), f"{base}.json")

    try:
        result = parse_gp_file(input_path)
        with open(out_path, "w", encoding="utf-8") as f:
            if args.pretty:
                json.dump(result, f, indent=2, ensure_ascii=False)
            else:
                json.dump(result, f, separators=(",", ":"), ensure_ascii=False)
        print(f"✅ Exported to: {out_path}")
        print(f"   ticksPerBeat: {result.get('ticksPerBeat', UNITY_TICKS_PER_BEAT)}")
    except Exception as e:
        print(f"❌ Error: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
