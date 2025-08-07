from flask import Flask, request, jsonify
import os
import tempfile
import traceback
from gp_to_json import parse_gp_file

app = Flask(__name__)

@app.route('/parse', methods=['POST'])
def parse():
    if 'file' not in request.files:
        return jsonify({"error": "No file uploaded"}), 400

    uploaded_file = request.files['file']
    filename = uploaded_file.filename or ""
    ext = os.path.splitext(filename)[1].lower()

    # Accept GP3/4/5 and GPX
    allowed = {'.gp3', '.gp4', '.gp5', '.gpx'}
    if ext not in allowed:
        return jsonify({"error": f"Unsupported file type: {ext}"}), 400

    # Save with the SAME extension so the parser picks the right decoder
    with tempfile.NamedTemporaryFile(delete=False, suffix=ext) as tmp:
        uploaded_file.save(tmp.name)
        try:
            parsed = parse_gp_file(tmp.name)
            return jsonify(parsed)
        except Exception as e:
            app.logger.exception("Guitar Pro parse failed")
            return jsonify({"error": str(e), "trace": traceback.format_exc()}), 500
        finally:
            try:
                os.unlink(tmp.name)
            except OSError:
                pass

@app.route('/health', methods=['GET'])
def health():
    return jsonify({"ok": True}), 200

if __name__ == '__main__':
    port = int(os.environ.get("PORT", 5000))
    app.run(host='0.0.0.0', port=port)
