// Program.cs
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Go2HDR
{
    #region DisplayConfig P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx;
        public uint outputTechnology; public int rotation; public int scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate; public int scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo; public uint flags; }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_MODE_INFO { public int infoType; public uint id; public LUID adapterId; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] modeInfoData; }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public int type; public uint size; public LUID adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_SET_SDR_WHITE_LEVEL { public DISPLAYCONFIG_DEVICE_INFO_HEADER header; public uint SDRWhiteLevel; public byte finalValue; }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO { public DISPLAYCONFIG_DEVICE_INFO_HEADER header; public uint value; public int colorEncoding; public int bitsPerColorChannel; }

    #endregion

    static class Program
    {
        [STAThread]
        static void Main()
        {
            using var mutex = new System.Threading.Mutex(true, "Go2HDR_SingleInstance", out bool isNew);
            if (!isNew) return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApp());
        }
    }

    class TrayApp : ApplicationContext
    {
        // ── DisplayConfig constants ────────────────────────────────────────
        const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        const int DC_SET_SDR_WHITE_LEVEL = unchecked((int)0xFFFFFFEE);
        const int DC_GET_ADVANCED_COLOR_INFO = 9;
        const uint OUTPUT_TECHNOLOGY_INTERNAL = 0x80000000;

        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint pc, out uint mc);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint pc, [Out] DISPLAYCONFIG_PATH_INFO[] paths, ref uint mc, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr tid);
        [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_SDR_WHITE_LEVEL r);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO r);

        // ── App constants ──────────────────────────────────────────────────
        const string TASK_NAME = "Go2HDR";
        const string REG_KEY = @"Software\Go2HDR";
        const string REG_DONT_ASK = "DontAskAutostart";

        // ── State ──────────────────────────────────────────────────────────
        readonly NotifyIcon _tray;
        readonly ToolStripMenuItem _autostartItem;
        readonly System.Windows.Forms.Timer _pollTimer;
        ManagementEventWatcher? _watcher;
        bool _hdrActive;

        public TrayApp()
        {
            _autostartItem = new ToolStripMenuItem(string.Empty, null, OnAutostartToggle);

            var menu = new ContextMenuStrip();
            menu.Items.Add(_autostartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => { Cleanup(); Application.Exit(); });
            menu.Opening += (_, _) => _autostartItem.Text = IsTaskInstalled()
                ? "Remove autostart"
                : "Install autostart";

            _tray = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                Text = "Go2HDR",
                Visible = true,
                ContextMenuStrip = menu
            };

            CheckFirstRun();

            _pollTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _pollTimer.Tick += (_, _) => UpdateHdrState();
            _pollTimer.Start();
            UpdateHdrState();
        }

        // ── First-run autostart prompt ─────────────────────────────────────

        void CheckFirstRun()
        {
            if (IsTaskInstalled()) return;

            using var key = Registry.CurrentUser.OpenSubKey(REG_KEY);
            if (key?.GetValue(REG_DONT_ASK) is int v && v == 1) return;

            using var dlg = new AutostartDialog();
            dlg.ShowDialog();

            if (dlg.DontAskAgain)
            {
                using var rk = Registry.CurrentUser.CreateSubKey(REG_KEY);
                rk.SetValue(REG_DONT_ASK, 1, RegistryValueKind.DWord);
            }

            if (dlg.DialogResult == DialogResult.Yes)
                InstallTask();
        }

        void OnAutostartToggle(object? sender, EventArgs e)
        {
            if (IsTaskInstalled()) RemoveTask();
            else InstallTask();
        }

        // ── HDR polling ────────────────────────────────────────────────────

        void UpdateHdrState()
        {
            bool hdrNow = IsBuiltInHdrActive();
            if (hdrNow == _hdrActive) return;

            _hdrActive = hdrNow;
            if (_hdrActive)
            {
                StartBrightnessWatcher();
                SyncCurrentBrightness();
            }
            else
            {
                StopBrightnessWatcher();
            }
        }

        bool IsBuiltInHdrActive()
        {
            try
            {
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pc, out uint mc) != 0) return false;
                var paths = new DISPLAYCONFIG_PATH_INFO[pc];
                var modes = new DISPLAYCONFIG_MODE_INFO[mc];
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pc, paths, ref mc, modes, IntPtr.Zero) != 0) return false;

                for (int i = 0; i < pc; i++)
                {
                    if (paths[i].targetInfo.outputTechnology != OUTPUT_TECHNOLOGY_INTERNAL) continue;

                    var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                    {
                        header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                        {
                            type = DC_GET_ADVANCED_COLOR_INFO,
                            size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
                            adapterId = paths[i].targetInfo.adapterId,
                            id = paths[i].targetInfo.id
                        }
                    };
                    if (DisplayConfigGetDeviceInfo(ref info) != 0) continue;
                    if ((info.value & 0x2) != 0) return true; // advancedColorEnabled bit
                }
            }
            catch { }
            return false;
        }

        // ── Brightness watcher ─────────────────────────────────────────────

        void SyncCurrentBrightness()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightness");
                using var instances = searcher.Get();

                foreach (var instance in instances)
                {
                    byte brightness = (byte)((ManagementBaseObject)instance)["CurrentBrightness"];
                    SetSdrWhiteLevel(brightness);
                    break;
                }
            }
            catch { }
        }

        void StartBrightnessWatcher()
        {
            if (_watcher != null) return;
            try
            {
                _watcher = new ManagementEventWatcher(
                    new ManagementScope(@"root\wmi"),
                    new WqlEventQuery("SELECT * FROM __InstanceModificationEvent WITHIN 1 WHERE TargetInstance ISA 'WmiMonitorBrightness'"));
                _watcher.EventArrived += OnBrightnessChanged;
                _watcher.Start();
            }
            catch { _watcher = null; }
        }

        void StopBrightnessWatcher()
        {
            if (_watcher == null) return;
            _watcher.Stop();
            _watcher.Dispose();
            _watcher = null;
        }

        void OnBrightnessChanged(object sender, EventArrivedEventArgs e)
        {
            byte brightness = (byte)((ManagementBaseObject)e.NewEvent["TargetInstance"])["CurrentBrightness"];
            SetSdrWhiteLevel(brightness);
        }

        // ── SDR White Level ────────────────────────────────────────────────

        static readonly byte[] SdrSliderMap = {
            // 0-46
            0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,
            // 47-100
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 13, 15, 16, 17, 19, 20, 22, 23, 25, 26, 28, 29, 31, 33, 34,
            36, 38, 40, 42, 44, 46, 48, 50, 53, 55, 57, 59, 62, 64, 66, 69, 71, 75, 77, 79, 82, 85, 88, 91, 94, 96, 100
        };

        void SetSdrWhiteLevel(byte brightness)
        {
            if (brightness > 100) brightness = 100;

            int sdrSliderValue = SdrSliderMap[brightness];

            int nits = 80 + (sdrSliderValue * 4);

            uint raw = (uint)(nits * 1000 / 80);

            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pc, out uint mc) != 0) return;
            var paths = new DISPLAYCONFIG_PATH_INFO[pc];
            var modes = new DISPLAYCONFIG_MODE_INFO[mc];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pc, paths, ref mc, modes, IntPtr.Zero) != 0) return;

            for (int i = 0; i < pc; i++)
            {
                var packet = new DISPLAYCONFIG_SET_SDR_WHITE_LEVEL
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DC_SET_SDR_WHITE_LEVEL,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SET_SDR_WHITE_LEVEL>(),
                        adapterId = paths[i].targetInfo.adapterId,
                        id = paths[i].targetInfo.id
                    },
                    SDRWhiteLevel = raw,
                    finalValue = 1
                };
                DisplayConfigSetDeviceInfo(ref packet);
            }
        }

        // ── Task Scheduler ─────────────────────────────────────────────────

        static string GetAppDataExePath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Go2HDR");
            return Path.Combine(dir, "Go2HDR.exe");
        }

        static bool IsTaskInstalled()
            => Exec("schtasks", $"/query /tn \"{TASK_NAME}\"", false) == 0;

        static void InstallTask()
        {
            string currentExe = Process.GetCurrentProcess().MainModule!.FileName;
            string appDataExe = GetAppDataExePath();

            if (!string.Equals(currentExe, appDataExe, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(appDataExe)!);
                File.Copy(currentExe, appDataExe, true);
            }

            Exec("schtasks",
                $"/create /tn \"{TASK_NAME}\" " +
                $"/tr \"\\\"{appDataExe}\\\"\" " +
                $"/sc onlogon /rl highest /f " +
                $"/ru \"{Environment.UserDomainName}\\{Environment.UserName}\"",
                true);
        }

        static void RemoveTask()
        {
            Exec("schtasks", $"/delete /tn \"{TASK_NAME}\" /f", true);

            string appDataExe = GetAppDataExePath();
            string currentExe = Process.GetCurrentProcess().MainModule!.FileName;

            if (!string.Equals(currentExe, appDataExe, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(appDataExe)) File.Delete(appDataExe);
                }
                catch { }
            }
        }

        static int Exec(string exe, string args, bool requireAdmin)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                CreateNoWindow = true,
                UseShellExecute = requireAdmin,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            if (requireAdmin)
            {
                psi.Verb = "runas";
            }

            try
            {
                var p = Process.Start(psi)!;
                p.WaitForExit();
                return p.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return -1;
            }
        }

        // ── Cleanup ────────────────────────────────────────────────────────

        void Cleanup()
        {
            _pollTimer.Stop();
            StopBrightnessWatcher();
            _tray.Visible = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Cleanup();
            base.Dispose(disposing);
        }
    }

    // ── Autostart dialog ───────────────────────────────────────────────────

    sealed class AutostartDialog : Form
    {
        public bool DontAskAgain => _check.Checked;

        readonly CheckBox _check;

        public AutostartDialog()
        {
            Text = "Go2HDR";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(390, 162);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var font = new Font("Segoe UI", 9f);

            Controls.Add(new Label
            {
                Text = "Would you like Go2HDR to start automatically with Windows?\n\n" +
                           "A scheduled task will be created with elevated privileges,\n" +
                           "ensuring the app always runs regardless of power state.",
                Location = new Point(16, 14),
                Size = new Size(358, 70),
                Font = font
            });

            _check = new CheckBox
            {
                Text = "Don't ask again",
                Location = new Point(16, 90),
                Size = new Size(180, 22),
                Font = font
            };
            Controls.Add(_check);

            var yes = new Button { Text = "Yes", DialogResult = DialogResult.Yes, Location = new Point(202, 124), Size = new Size(80, 28), Font = font };
            var no = new Button { Text = "No", DialogResult = DialogResult.No, Location = new Point(294, 124), Size = new Size(80, 28), Font = font };

            Controls.Add(yes);
            Controls.Add(no);
            AcceptButton = yes;
            CancelButton = no;
        }
    }
}