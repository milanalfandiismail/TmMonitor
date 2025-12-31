from flask import Flask, request, jsonify, render_template
from datetime import datetime

app = Flask(__name__)

# Ini database sementara (disimpan di RAM)
# Format: { "NAMA-PC": { data... }, "PC-LAIN": { data... } }
pc_data_store = {}

@app.route('/')
def index():
    return render_template('dashboard.html')

# Endpoint untuk menerima data dari C# (POST)
@app.route('/api/monitor', methods=['POST'])
def receive_data():
    data = request.json
    machine_name = data.get('MachineName')
    
    # Tambahkan waktu server menerima data (untuk status Last Seen)
    data['ServerTime'] = datetime.now().strftime("%H:%M:%S")
    
    # Simpan/Update data berdasarkan Nama PC
    pc_data_store[machine_name] = data
    
    print(f"Update dari {machine_name}...")
    return jsonify({"status": "success"}), 200

# Endpoint untuk Frontend mengambil data terbaru (GET)
@app.route('/api/data', methods=['GET'])
def get_data():
    # Ubah dictionary ke list supaya bisa dibaca JavaScript (Array)
    return jsonify(list(pc_data_store.values()))

if __name__ == '__main__':
    # Host 0.0.0.0 supaya bisa diakses komputer lain di jaringan
    app.run(host='0.0.0.0', port=5000, debug=True)