using System;
using System.IO; 
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Http; 
using System.Text;
using System.Text.Json; 
using System.Threading;
using System.Threading.Tasks; 
using LibreHardwareMonitor.Hardware;

namespace MonitoringAgent
{
    public class PcMetricPayload
    {
        public string MachineName { get; set; }
        public float CpuUsage { get; set; }
        public float CpuTemp { get; set; }
        public float GpuTemp { get; set; }
        public string NicSpeed { get; set; }
        public string TotalRam { get; set; }
        public DateTime Timestamp { get; set; }
    }

    class Program
    {
        private static readonly HttpClient client = new HttpClient();
        
        // Default URL (akan ditimpa oleh config.ini jika ada)
        private static string SERVER_ENDPOINT = "http://127.0.0.1:5000/api/monitor"; 
        private const string CONFIG_FILE = "config.ini";

        static async Task Main(string[] args)
        {
            // 1. BACA CONFIG DULU
            LoadConfiguration(); 

            // 2. SETUP HARDWARE (Mata-mata Sensor)
            Computer computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsNetworkEnabled = false
            };

            try 
            { 
                computer.Open(); 
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"Error: {ex.Message}. WAJIB RUN AS ADMIN!"); 
                Console.ReadLine();
                return; 
            }

            Console.WriteLine("==========================================");
            Console.WriteLine($" TARGET SERVER: {SERVER_ENDPOINT}");
            Console.WriteLine("==========================================");

            // 3. LOOPING UTAMA
            while (true)
            {
                // --- BAGIAN A: BACA SENSOR (Update Data Real) ---
                float cpuLoad = 0; 
                float cpuTemp = 0; 
                float gpuTemp = 0;
                string ramTotalInfo = "0 GB"; // Variable penampung RAM

                // Loop Hardware
                foreach (IHardware hardware in computer.Hardware)
                {
                    hardware.Update(); // WAJIB: Refresh data sensor

                    // LOGIKA CPU
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        var loadSensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Total"));
                        if (loadSensor != null) cpuLoad = loadSensor.Value.GetValueOrDefault();
                        
                        var tempSensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && (s.Name.Contains("Package") || s.Name.Contains("Tdie"))) 
                                      ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Core"));
                        
                        if (tempSensor != null) cpuTemp = tempSensor.Value.GetValueOrDefault();
                    }

                    // LOGIKA GPU
                    if (hardware.HardwareType.ToString().Contains("Gpu"))
                    {
                        var gpuSensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Core"));
                        if (gpuSensor != null) gpuTemp = gpuSensor.Value.GetValueOrDefault();
                    }

                    // LOGIKA RAM (PERBAIKAN DISINI)
                    if (hardware.HardwareType == HardwareType.Memory)
                    {
                        // LibreHardwareMonitor memisahkan Used & Available. Kita cari keduanya.
                        var usedSensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Used"));
                        var availSensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Available"));

                        if (usedSensor != null && availSensor != null)
                        {
                            // Jumlahkan Used + Available = Total RAM
                            float totalRamGb = usedSensor.Value.GetValueOrDefault() + availSensor.Value.GetValueOrDefault();
                            ramTotalInfo = $"{Math.Round(totalRamGb, 1)} GB";
                        }
                    }
                }

                // LOGIKA NETWORK SPEED
                string linkSpeed = "Disconnected";
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus == OperationalStatus.Up && 
                        nic.NetworkInterfaceType != NetworkInterfaceType.Loopback && 
                        nic.Speed > 0)
                    {
                        double speedM = nic.Speed / 1000000.0;
                        linkSpeed = speedM >= 1000 ? $"{(speedM/1000):0.#} Gbps" : $"{speedM:0} Mbps";
                        break; 
                    }
                }

                // --- BAGIAN B: BUNGKUS DATA ---
                var payload = new PcMetricPayload
                {
                    MachineName = Environment.MachineName,
                    CpuUsage = (float)Math.Round(cpuLoad, 1),
                    CpuTemp = (float)Math.Round(cpuTemp, 0),
                    GpuTemp = (float)Math.Round(gpuTemp, 0),
                    NicSpeed = linkSpeed,
                    TotalRam = ramTotalInfo,
                    Timestamp = DateTime.Now
                };

                // --- BAGIAN C: KIRIM DATA ---
                try
                {
                    string jsonString = JsonSerializer.Serialize(payload);
                    var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
                    
                    var response = await client.PostAsync(SERVER_ENDPOINT, content);
                    
                    if (response.IsSuccessStatusCode)
                        Console.WriteLine($"[OK] {payload.MachineName} | CPU: {payload.CpuUsage}% | RAM: {payload.TotalRam}");
                    else
                        Console.WriteLine($"[FAIL] Server Reject: {response.StatusCode}");
                }
                catch
                {
                    Console.WriteLine($"[ERR] Gagal konek ke: {SERVER_ENDPOINT}");
                }

                await Task.Delay(60000); 
            }
        }

        // FUNGSI BACA CONFIG (Sama seperti sebelumnya)
        private static void LoadConfiguration()
        {
            try
            {
                if (!File.Exists(CONFIG_FILE))
                {
                    Console.WriteLine("Config file tidak ditemukan. Membuat baru...");
                    string defaultConfig = "ServerUrl=http://127.0.0.1:5000/api/monitor";
                    File.WriteAllText(CONFIG_FILE, defaultConfig);
                    SERVER_ENDPOINT = "http://127.0.0.1:5000/api/monitor";
                }
                else
                {
                    string[] lines = File.ReadAllLines(CONFIG_FILE);
                    foreach (string line in lines)
                    {
                        if (line.Trim().StartsWith("ServerUrl="))
                        {
                            string url = line.Split('=')[1].Trim();
                            if (!string.IsNullOrEmpty(url)) SERVER_ENDPOINT = url;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Gagal baca config: {ex.Message}. Default loaded.");
            }
        }
    }
}