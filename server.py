from flask import Flask, request, jsonify
import os
import tempfile
from gp_to_json import parse_gp_file

app = Flask(__name__)

@app.route('/parse', methods=['POST'])
def parse():
    if 'file' not in request.files:
        return jsonify({"error": "No file uploaded"}), 400

    uploaded_file = request.files['file']

    if not uploaded_file.filename.lower().endswith(('.gp3', '.gp4', '.gp5')):
        return jsonify({"error": "Unsupported file type"}), 400

    with tempfile.NamedTemporaryFile(delete=False, suffix=".gp") as tmp:
        uploaded_file.save(tmp.name)
        try:
            parsed = parse_gp_file(tmp.name)
            return jsonify(parsed)
        except Exception as e:
            return jsonify({"error": str(e)}), 500
        finally:
            os.unlink(tmp.name)

if __name__ == '__main__':
    port = int(os.environ.get("PORT", 5000))
    app.run(host='0.0.0.0', port=port)
