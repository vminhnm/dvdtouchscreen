// DiverTouch - settings panel + entry point. C# 5 compatible.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace DiverTouch
{
    public class Panel2 : Form
    {
        Settings S; Overlay O; Pad P;
        CheckBox cbOverlay, cbFixed, cbDpad, cbCancel, cbEdit;
        TrackBar tbSize, tbOpacity;
        ComboBox cbMonitor;
        Label status;
        TableLayoutPanel root;
        public bool Quit;

        public Panel2(Settings s, Overlay o, Pad p)
        {
            S = s; O = o; P = p;
            Text = "dvdtouchscreen";
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 13f);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            AutoScroll = true;
            BackColor = Color.White;

            float k;
            using (Graphics g = CreateGraphics()) k = g.DpiX / 96f;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = (int)(470 * k), h = (int)(760 * k);
            if (h > wa.Height * 0.92) h = (int)(wa.Height * 0.92);
            if (w > wa.Width * 0.9) w = (int)(wa.Width * 0.9);
            ClientSize = new Size(w, h);
            int inner = w - SystemInformation.VerticalScrollBarWidth - 24;

            root = new TableLayoutPanel();
            root.ColumnCount = 1;
            root.Dock = DockStyle.Top;
            root.AutoSize = true;
            root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            root.Padding = new Padding(12);
            root.Width = w - SystemInformation.VerticalScrollBarWidth;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, inner));
            Controls.Add(root);

            status = new Label();
            status.AutoSize = true; status.MaximumSize = new Size(inner, 0);
            status.Font = new Font(Font, FontStyle.Bold);
            Add(status);

            cbOverlay = Chk("Touch controls on", true, inner);
            cbEdit = Chk("Edit layout (move / resize buttons)", false, inner);
            cbFixed = Chk("Fixed left stick (off = floating)", S.FixedStick, inner);
            cbDpad = Chk("Show D-pad", S.ShowDpad, inner);
            cbCancel = Chk("Cancel zone while aiming", S.CancelZone, inner);

            Add(Lbl("Button size (all)"));
            tbSize = Bar(30, 150, (int)(S.Scale * 100));
            Add(Lbl("Opacity"));
            tbOpacity = Bar(15, 100, (int)(S.Opacity * 100));

            Add(Lbl("Screen with the game"));
            cbMonitor = new ComboBox(); cbMonitor.DropDownStyle = ComboBoxStyle.DropDownList;
            for (int i = 0; i < Screen.AllScreens.Length; i++)
            {
                Rectangle b = Screen.AllScreens[i].Bounds;
                cbMonitor.Items.Add((i + 1) + ": " + b.Width + "x" + b.Height + (Screen.AllScreens[i].Primary ? " (main)" : ""));
            }
            cbMonitor.SelectedIndex = Math.Min(S.Monitor, cbMonitor.Items.Count - 1);
            cbMonitor.Dock = DockStyle.Top; Add(cbMonitor);

            FlowLayoutPanel row1 = new FlowLayoutPanel();
            row1.AutoSize = true; row1.Margin = new Padding(0, 14, 0, 0);
            Button save = Btn("Save"); Button rl = Btn("Reset layout");
            row1.Controls.Add(save); row1.Controls.Add(rl);
            Add(row1);
            save.Click += delegate { Pull(); S.Save(); status.Text = "Saved."; };
            rl.Click += delegate { S.ResetLayout(); O.Redraw(); };

            FlowLayoutPanel row2 = new FlowLayoutPanel();
            row2.AutoSize = true;
            Button hide = Btn("Close panel"); Button quit = Btn("Quit dvdtouchscreen");
            row2.Controls.Add(hide); row2.Controls.Add(quit);
            Add(row2);
            hide.Click += delegate { Hide(); };
            quit.Click += delegate { Quit = true; Close(); };

            cbOverlay.CheckedChanged += Apply; cbEdit.CheckedChanged += Apply; cbFixed.CheckedChanged += Apply;
            cbDpad.CheckedChanged += Apply; cbCancel.CheckedChanged += Apply;
            tbSize.ValueChanged += Apply; tbOpacity.ValueChanged += Apply;
            cbMonitor.SelectedIndexChanged += Apply;
            RefreshStatus();
        }

        void Add(Control c)
        {
            int row = root.RowCount++;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(c, 0, row);
        }
        CheckBox Chk(string t, bool v, int inner)
        {
            CheckBox c = new CheckBox(); c.Text = t; c.Checked = v; c.AutoSize = true;
            c.MaximumSize = new Size(inner, 0);
            c.Margin = new Padding(3, 8, 3, 8);
            Add(c); return c;
        }
        Label Lbl(string t)
        {
            Label l = new Label(); l.Text = t; l.AutoSize = true; l.Margin = new Padding(3, 10, 3, 2);
            return l;
        }
        TrackBar Bar(int min, int max, int v)
        {
            TrackBar t = new TrackBar(); t.Minimum = min; t.Maximum = max;
            t.Value = Math.Max(min, Math.Min(max, v)); t.TickFrequency = 10; t.Dock = DockStyle.Top;
            Add(t); return t;
        }
        Button Btn(string t)
        {
            Button b = new Button(); b.Text = t; b.AutoSize = true; b.Padding = new Padding(10, 6, 10, 6); return b;
        }

        public void RefreshStatus()
        {
            status.Text = P.Ok ? "Virtual Xbox 360 controller: connected" :
                "Virtual controller NOT available: " + P.Error + "\r\nInstall ViGEmBus, then restart.";
            status.ForeColor = P.Ok ? Color.DarkGreen : Color.Firebrick;
        }

        void Pull()
        {
            S.Scale = tbSize.Value / 100f;
            S.Opacity = tbOpacity.Value / 100f;
            S.FixedStick = cbFixed.Checked;
            S.ShowDpad = cbDpad.Checked;
            S.CancelZone = cbCancel.Checked;
        }

        void Apply(object sender, EventArgs e)
        {
            Pull();
            if (sender == cbMonitor && cbMonitor.SelectedIndex >= 0) { S.Monitor = cbMonitor.SelectedIndex; O.ApplyMonitor(); }
            if (cbOverlay.Checked != O.Visible) { if (cbOverlay.Checked) O.Show(); else O.Hide(); }
            if (O.EditMode != cbEdit.Checked) O.EditMode = cbEdit.Checked;
            O.Redraw();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!Quit) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            Mutex mx = new Mutex(true, "dvdtouchscreen_SingleInstance", out created);
            if (!created) { MessageBox.Show("dvdtouchscreen is already running."); return; }

            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string dll = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Nefarius.ViGEm.Client.dll");
            Pad pad = new Pad();
            pad.Init(dll);

            Settings s = Settings.Load();
            Overlay o = new Overlay(s, pad);
            Panel2 panel = new Panel2(s, o, pad);
            o.OpenPanel += delegate
            {
                panel.RefreshStatus(); panel.Show(); panel.TopMost = true; panel.Activate();
            };

            NotifyIcon tray = new NotifyIcon();
            tray.Icon = SystemIcons.Application; tray.Text = "dvdtouchscreen"; tray.Visible = true;
            ContextMenu cm = new ContextMenu();
            cm.MenuItems.Add("Settings", delegate { panel.Show(); panel.Activate(); });
            cm.MenuItems.Add("Quit", delegate { panel.Quit = true; panel.Close(); Application.Exit(); });
            tray.ContextMenu = cm;
            tray.DoubleClick += delegate { panel.Show(); panel.Activate(); };

            System.Windows.Forms.Timer top = new System.Windows.Forms.Timer();
            top.Interval = 1500; top.Tick += delegate { if (o.Visible) o.KeepOnTop(); if (panel.Visible) Native.KeepTop(panel.Handle); };
            top.Start();

            o.Show();
            panel.Show();
            if (!pad.Ok) MessageBox.Show("Could not create the virtual Xbox 360 controller:\r\n" + pad.Error +
                "\r\n\r\nInstall ViGEmBus (github.com/nefarius/ViGEmBus/releases) and restart.\r\nThe overlay will still show, but nothing is sent to the game.", "dvdtouchscreen");

            Application.Run(panel);
            s.Save();
            tray.Visible = false;
            pad.Dispose();
            GC.KeepAlive(mx);
        }
    }
}
