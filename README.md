# Go2HDR for Lenovo Legion Go 2 ☀️

**Go2HDR** is a lightweight, system-tray utility specifically designed for the **Lenovo Legion Go 2**. It automatically synchronizes the SDR White Level (SDR brightness) with the device's actual hardware brightness whenever Windows HDR is active. 

When you enable HDR in Windows, standard desktop apps (SDR content) can often look overly bright or washed out, requiring manual adjustment of the "SDR content brightness" slider. Go2HDR solves this by running quietly in the background, listening for hardware brightness changes, and instantly updating the SDR white level to match your Legion Go 2's screen brightness perfectly.

## ✨ Features

* **Tailored for Legion Go 2:** The brightness curve is not based on generic linear math. It uses a custom, empirically tested 0-100 mapping table specifically calibrated for the Legion Go 2 display to translate system brightness into standard Nits (80 to 480 nits) accurately.
* **Automatic HDR Detection:** Continuously monitors your display topology. It only activates the brightness listener when an HDR signal (Advanced Color) is detected.
* **Real-time Synchronization:** Uses Windows Management Instrumentation (WMI) to instantly catch hardware brightness changes (via Legion Space hotkeys, Windows quick settings, or physical buttons).
* **Zero UAC Nagging:** Features an "idiot-proof" autostart mechanism. It securely copies itself to `%LocalAppData%` and sets up a high-privilege Task Scheduler entry, allowing it to start silently with Windows without triggering User Account Control (UAC) prompts.

## 🚀 How to use

1. Download the latest `Go2HDR.exe` from the Releases page.
2. Run the executable.
3. On the first run, it will ask if you want it to start automatically with Windows. Click **Yes** (you will see a brief UAC prompt to create the scheduled task).
4. That's it! The app will sit in your system tray. Whenever you turn on HDR, your SDR brightness will perfectly match your hardware brightness.

## 🛠️ Technical Details

Go2HDR is written in C# and interacts directly with the low-level Windows API:
* **`user32.dll` (DisplayConfig):** Used to query active display paths, check the `advancedColorEnabled` flag, and inject the raw `DISPLAYCONFIG_SET_SDR_WHITE_LEVEL` packets.
* **`WqlEventQuery` (WMI):** Used to attach an asynchronous listener to the `WmiMonitorBrightness` class.
* **`schtasks` (Task Scheduler):** Used to programmatically install/remove the elevated startup task.

### The Autostart Mechanism
To ensure the app survives being moved or deleted from the Downloads folder, the autostart installation automatically creates an isolated directory in `%LocalAppData%\Go2HDR`, copies the executable there, and binds the Scheduled Task to that secure location.

## 📄 License
This project is licensed under the MIT License - see the LICENSE file for details.
