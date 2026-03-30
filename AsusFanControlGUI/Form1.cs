using AsusFanControl.Core;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AsusFanControlGUI
{
    public partial class Form1 : Form
    {
        AsusControl asusControl = new AsusControl();
        int fanSpeed = 0;
        NotifyIcon trayIcon;
        FanCurve currentFanCurve;

        private CancellationTokenSource refreshCts;
        private Task refreshTask;


        private void timerRefreshStats()
        {
            refreshCts?.Cancel();
            refreshCts = null;

            bool auto = checkBoxAuto.Checked;

            if (!auto)
                return;

            int interval = Math.Max((int)numericUpdateInterval.Value, 500);

            refreshCts = new CancellationTokenSource();
            refreshTask = RunRefreshLoop(interval, refreshCts.Token);
        }

        private async Task RunRefreshLoop(int interval, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Run(() => DoHardwareWork(), token);
                    await Task.Delay(interval, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }


        private void DoHardwareWork()
        {

            if (!checkBoxAuto.Checked || asusControl == null)
                return;

            ulong tempU = asusControl.Thermal_Read_Cpu_Temperature();

            // Guard against hardware error codes (valid CPU temp: 0–120 °C)
            if (tempU > 120)
                return;

            int temp = (int)tempU;

            int targetSpeed = currentFanCurve.GetTargetSpeed(temp);

            if (Math.Abs(targetSpeed - fanSpeed) > 2)
            {
                fanSpeed = targetSpeed;
                asusControl.SetFanSpeeds(targetSpeed);
            }

            if (this.Visible)
            {
                BeginInvoke((Action)(() =>
                {
                    labelValue.Text = $"{targetSpeed}% (Auto)";
                    labelCPUTemp.Text = $"{tempU}";
                    trackBarFanSpeed.Value = targetSpeed;
                    //labelRPM.Text = string.Join(" ", asusControl.GetFanSpeeds());
                }));
            }
        }

        public Form1()
        {
            InitializeComponent();

            AppDomain.CurrentDomain.ProcessExit += new EventHandler(OnProcessExit);
            // Watchdog for crash
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { try { if (asusControl != null) asusControl.ResetToDefaultAsync().GetAwaiter().GetResult(); } catch { } };
            Application.ThreadException += (s, e) => { try { if (asusControl != null) asusControl.ResetToDefaultAsync().GetAwaiter().GetResult(); } catch { } };

            toolStripMenuItemTurnOffControlOnExit.Checked = Properties.Settings.Default.turnOffControlOnExit;
            toolStripMenuItemForbidUnsafeSettings.Checked = Properties.Settings.Default.forbidUnsafeSettings;
            toolStripMenuItemMinimizeToTrayOnClose.Checked = Properties.Settings.Default.minimizeToTrayOnClose;
            trackBarFanSpeed.Value = Properties.Settings.Default.fanSpeed;

            checkBoxAuto.Checked = Properties.Settings.Default.autoMode;
            numericUpdateInterval.Value = Properties.Settings.Default.updateInterval;
            currentFanCurve = FanCurve.FromString(Properties.Settings.Default.fanCurve);

            if (currentFanCurve.Points.Count == 0)
            {
                 // Default curve
                currentFanCurve.Points.Add(new FanCurvePoint(30, 0));
                currentFanCurve.Points.Add(new FanCurvePoint(50, 22));
                currentFanCurve.Points.Add(new FanCurvePoint(60, 50));
                currentFanCurve.Points.Add(new FanCurvePoint(70, 65));
                currentFanCurve.Points.Add(new FanCurvePoint(90, 100));
            }

            updateUIState();
        }

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                {
                    components.Dispose();
                }

                trayIcon?.Dispose();
                trayIcon = null;

                // Custom cleanup
                if (asusControl != null)
                {
                    // Ensure fans are reset if configured to do so
                    if (Properties.Settings.Default.turnOffControlOnExit)
                    {
                        try
                        {
                            asusControl.ResetToDefaultAsync().GetAwaiter().GetResult();
                        }
                        catch
                        {
                            // Swallow exceptions during disposal to prevent crashing
                        }
                    }

                    try
                    {
                        asusControl.Dispose();
                    }
                    catch { }

                    asusControl = null; // Prevent use-after-dispose
                }
            }
            base.Dispose(disposing);
        }

        private void updateUIState()
        {
            bool auto = checkBoxAuto.Checked;

            if (auto)
            {
                checkBoxTurnOn.Checked = false;
                checkBoxTurnOn.Enabled = false;
                trackBarFanSpeed.Enabled = false;
            }
            else
            {
                checkBoxTurnOn.Enabled = true;
                trackBarFanSpeed.Enabled = true;
            }

            timerRefreshStats();
        }

        private void OnProcessExit(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.turnOffControlOnExit && asusControl != null)
            {
                try
                {
                    asusControl.ResetToDefaultAsync().GetAwaiter().GetResult();
                }
                catch
                {
                    // Ignore exceptions during shutdown
                }
            }
            // Do not dispose here to avoid race with Form.Dispose(bool) or double-dispose.
            // asusControl.Dispose() will be called when the Form is disposed.
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Timer already started in updateUIState called from ctor, but calling it here is safe too
            timerRefreshStats();
        }




        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (Properties.Settings.Default.minimizeToTrayOnClose && Visible)
            {
                if(trayIcon == null)
                {
                    trayIcon = new NotifyIcon()
                    {
                        Icon = Icon,
                        ContextMenu = new ContextMenu(new MenuItem[] {
                            new MenuItem("Show", (s1, e1) =>
                            {
                                trayIcon.Visible = false;
                                Show();
                            }),
                            new MenuItem("Exit", (s1, e1) =>
                            {
                                Close();
                                trayIcon.Visible = false;
                                Application.Exit();
                            }),
                        }),
                    };

                    trayIcon.MouseClick += (s1, e1) =>
                    {
                        if (e1.Button != MouseButtons.Left)
                            return;

                        trayIcon.Visible = false;
                        Show();
                    };
                }

                trayIcon.Visible = true;
                e.Cancel = true;
                Hide();
            }
            else
            {
                refreshCts?.Cancel();
            }
        }

        private void toolStripMenuItemTurnOffControlOnExit_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.turnOffControlOnExit = toolStripMenuItemTurnOffControlOnExit.Checked;
            Properties.Settings.Default.Save();
        }

        private void toolStripMenuItemForbidUnsafeSettings_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.forbidUnsafeSettings = toolStripMenuItemForbidUnsafeSettings.Checked;
            Properties.Settings.Default.Save();
        }

        private void toolStripMenuItemMinimizeToTrayOnClose_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.minimizeToTrayOnClose = toolStripMenuItemMinimizeToTrayOnClose.Checked;
            Properties.Settings.Default.Save();
        }

        private void toolStripMenuItemCheckForUpdates_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/Tommixoft/AsusFanControl-PRO/releases");
        }

        private void setFanSpeed()
        {
            if (checkBoxAuto.Checked || asusControl == null)
                return;

            var value = trackBarFanSpeed.Value;
            Properties.Settings.Default.fanSpeed = value;
            Properties.Settings.Default.Save();

            if (!checkBoxTurnOn.Checked)
                value = 0;

            if (value == 0)
                labelValue.Text = "turned off";
            else
                labelValue.Text = value.ToString();

            if (fanSpeed == value)
                return;

            fanSpeed = value;
            asusControl.SetFanSpeeds(value);
        }

        private void checkBoxTurnOn_CheckedChanged(object sender, EventArgs e)
        {
            setFanSpeed();
        }

        private void trackBarFanSpeed_MouseCaptureChanged(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.forbidUnsafeSettings)
            {
                if (trackBarFanSpeed.Value < 40)
                    trackBarFanSpeed.Value = 40;
                else if (trackBarFanSpeed.Value > 99)
                    trackBarFanSpeed.Value = 99;
            }

            setFanSpeed();
        }

        private void trackBarFanSpeed_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Left && e.KeyCode != Keys.Right)
                return;

            trackBarFanSpeed_MouseCaptureChanged(sender, e);
        }

        private void checkBoxAuto_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.autoMode = checkBoxAuto.Checked;
            Properties.Settings.Default.Save();
            updateUIState();
        }

        private void buttonEditCurve_Click(object sender, EventArgs e)
        {
            var editor = new FanCurveEditor(currentFanCurve);
            if (editor.ShowDialog() == DialogResult.OK)
            {
                currentFanCurve = editor.ResultCurve;
                Properties.Settings.Default.fanCurve = currentFanCurve.ToString();
                Properties.Settings.Default.Save();
            }
        }

        private void numericUpdateInterval_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.updateInterval = Math.Max((int)numericUpdateInterval.Value, 500);
            Properties.Settings.Default.Save();
            timerRefreshStats();
        }

    }
}
