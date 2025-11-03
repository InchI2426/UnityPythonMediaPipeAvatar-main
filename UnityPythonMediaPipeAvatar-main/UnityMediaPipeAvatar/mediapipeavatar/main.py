import socket
import threading
import time
import struct
import global_vars
from body import BodyThread
from sys import exit
quit_flag = False
# ============================================================
def wait_for_unity(host="127.0.0.1", port=52733, timeout=5):
    print("[Python] Waiting for Unity to connect...")
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.settimeout(0.5)
    start = time.time()
    while time.time() - start < timeout:
        try:
            s.sendto(b"__ping__", (host, port))
            time.sleep(0.3)
        except Exception:
            pass
    s.close()
    print("[Python] Done waiting. Starting BodyThread...")
# ============================================================
def quit_listener(port=54321): #ฟังก์ชันรอฟังสัญญาณปิดจาก Unity
    global quit_flag
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.bind(("127.0.0.1", port))
    s.settimeout(1.0)
    print(f"[Python] Listening for quit signal on port {port}...")
    while not quit_flag:
        try:
            data, addr = s.recvfrom(1024)
            msg = data.decode().strip()
            if msg == "__QUIT__":
                print("[Python] Received quit signal from Unity.")
                quit_flag = True
                break
        except socket.timeout:
            continue
    s.close()
# ============================================================
wait_for_unity()

# ✅ สตาร์ทตัวรับสัญญาณปิด
listener_thread = threading.Thread(target=quit_listener, daemon=True)
listener_thread.start()

# ✅ สตาร์ท MediaPipe BodyThread
thread = BodyThread()
thread.start()

# ✅ ทำงานหลักจนกว่าจะมีสัญญาณปิด
try:
    while not quit_flag and not global_vars.KILL_THREADS:
        time.sleep(0.1)
except KeyboardInterrupt:
    print("[Python] KeyboardInterrupt received, exiting...")

# ✅ ปิดระบบอย่างปลอดภัย
print("[Python] Cleaning up resources...")
global_vars.KILL_THREADS = True
quit_flag = True
time.sleep(0.5)
print("[Python] Exiting gracefully.")

if hasattr(thread, "stop"):
    thread.stop()
exit()
