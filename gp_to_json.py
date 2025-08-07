import guitarpro
import json
import os

def parse_gp_file(input_path):
    song = guitarpro.parse(input_path)

    export = {
        "title": song.title,
        "artist": song.artist,
        "tempo": song.tempo,
        "tracks": []
    }

    for track in song.tracks:
        track_data = {
            "name": track.name,
            "strings": [s.value for s in track.strings],
            "measures": []
        }

        for measure in track.measures:
            measure_data = {"voices": []}

            for voice in measure.voices:
                voice_data = {"beats": []}

                for beat in voice.beats:
                    beat_data = {
                        "start": beat.start,
                        "duration": beat.duration.time,
                        "notes": []
                    }

                    for note in beat.notes:
                        beat_data["notes"].append({
                            "string": note.string,
                            "fret": note.value  # correct property
                        })

                    voice_data["beats"].append(beat_data)

                measure_data["voices"].append(voice_data)

            track_data["measures"].append(measure_data)

        export["tracks"].append(track_data)

    return export

# 🧪 CLI usage for local testing
if __name__ == "__main__":
    import sys

    if len(sys.argv) < 2:
        print("Usage: python gp_to_json.py path_to_file.gpX")
        sys.exit(1)

    input_path = sys.argv[1]
    output_path = os.path.join(os.path.dirname(input_path), "output.json")

    try:
        result = parse_gp_file(input_path)
        with open(output_path, "w") as f:
            json.dump(result, f, indent=2)
        print("✅ Exported to:", output_path)
    except Exception as e:
        print("❌ Error:", e)
