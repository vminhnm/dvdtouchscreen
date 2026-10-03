// DiverTouch - the click-through, non-activating touch overlay. C# 5 compatible.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DiverTouch
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        public struct TOUCHINPUT
        {
            public int x, y;
            public IntPtr hSource;
            public int dwID, dwFlags, dwMask, dwTime;
            public IntPtr dwExtraInfo;
            public int cxContact, cyContact;
        }

        public const int WM_MOUSEACTIVATE = 0x21, WM_TOUCH = 0x240;
        public const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202;
        public const int TOUCHEVENTF_MOVE = 1, TOUCHEVENTF_DOWN = 2, TOUCHEVENTF_UP = 4;

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool RegisterTouchWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] public static extern bool GetTouchInputInfo(IntPtr hTouch, int cInputs, [In, Out] TOUCHINPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")] public static extern void CloseTouchInputHandle(IntPtr hTouch);
        [DllImport("user32.dll")] public static extern IntPtr GetMessageExtraInfo();
        [DllImport("user32.dll")] public static extern IntPtr SetCapture(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObj);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

        public static void KeepTop(IntPtr h)
        {
            SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }
    }

    public class Overlay : Form
    {
        public event EventHandler OpenPanel;

        public Settings S;
        public Pad PadDev;
        Rectangle scr;
        Bitmap bmp;
        Graphics g;
        bool edit;
        Element selected;
        float dragDX, dragDY;
        bool moved;
        Element resizeEl; int resizeId = int.MinValue;
        Dictionary<int, Element> touches = new Dictionary<int, Element>();
        Timer timer = new Timer();
        Stopwatch clock = Stopwatch.StartNew();
        List<KeyValuePair<long, Action>> sched = new List<KeyValuePair<long, Action>>();
        bool needDraw = true;
        bool overCancel;
        Element aimEl;

        public bool EditMode
        {
            get { return edit; }
            set { edit = value; ReleaseTouches(); needDraw = true; }
        }

        public Overlay(Settings s, Pad pad)
        {
            S = s; PadDev = pad;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ApplyMonitor();
            timer.Interval = 12;
            timer.Tick += OnTick;
            timer.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x8 | 0x80 | 0x08000000; // LAYERED | TOPMOST | TOOLWINDOW | NOACTIVATE
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RegisterTouchWindow(Handle, 0);
        }

        public void ApplyMonitor()
        {
            Screen[] all = Screen.AllScreens;
            int i = S.Monitor;
            if (i < 0 || i >= all.Length) i = 0;
            scr = all[i].Bounds;
            Bounds = scr;
            if (g != null) g.Dispose();
            if (bmp != null) bmp.Dispose();
            bmp = new Bitmap(scr.Width, scr.Height, PixelFormat.Format32bppPArgb);
            g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            needDraw = true;
        }

        public void Redraw() { needDraw = true; }

        public void KeepOnTop() { if (IsHandleCreated) Native.KeepTop(Handle); }

        // ---------- geometry ----------
        float W { get { return scr.Width; } }
        float H { get { return scr.Height; } }
        float Rad(Element e) { return e.R * H * S.Scale * e.Size; }
        float RingF(Element e) { return e.Kind == Kind.Stick ? 1.5f : (e.Kind == Kind.RStick ? 1.3f : 1f); }
        float EditRing(Element e) { return RingF(e) * Rad(e) + 6; }
        PointF HandlePos(Element e) { float rr = EditRing(e) * 0.7071f; return new PointF(e.X * W + rr, e.Y * H + rr); }
        bool HandleHit(Element e, float x, float y) { PointF h = HandlePos(e); float dx = x - h.X, dy = y - h.Y; return Math.Sqrt(dx * dx + dy * dy) <= H * 0.03f; }
        bool Vis(Element e) { return !(e.Kind == Kind.Dpad && !S.ShowDpad); }

        RectangleF StickZone() { return new RectangleF(0, H * 0.30f, W * 0.42f, H * 0.70f); }

        PointF CancelCenter() { Element c = S.Find("cancel"); return new PointF(c.X * W, c.Y * H); }
        float CancelRad() { return Rad(S.Find("cancel")); }

        float HitR(Element e)
        {
            float r = Rad(e);
            if (e.Kind == Kind.Stick) return S.FixedStick || edit ? r * 1.5f : r;
            if (e.Kind == Kind.RStick) return r * 1.3f;
            return r * 1.2f;
        }

        Element Hit(float x, float y)
        {
            Element best = null; float bn = 1e9f;
            foreach (Element e in S.Items)
            {
                if (!Vis(e)) continue;
                if (e.Kind == Kind.Cancel && !edit) continue;
                if (!edit && e.Down) continue;
                if (!edit && e.Kind == Kind.Stick && !S.FixedStick) continue;
                float dx = x - e.X * W, dy = y - e.Y * H;
                float d = (float)Math.Sqrt(dx * dx + dy * dy), hr = HitR(e);
                if (d <= hr && d / hr < bn) { bn = d / hr; best = e; }
            }
            return best;
        }

        // ---------- pointer input ----------
        void ReleaseTouches()
        {
            foreach (Element e in S.Items) if (e.Down) EndElement(e, false);
            touches.Clear();
            resizeId = int.MinValue; resizeEl = null;
            selected = null;
            PadDev.Left(0, 0); PadDev.Right(0, 0);
            overCancel = false; aimEl = null;
        }

        public void PointerDown(int id, float x, float y)
        {
            if (touches.ContainsKey(id)) return;
            if (edit && selected != null && HandleHit(selected, x, y))
            {
                resizeEl = selected; resizeId = id; touches[id] = selected; needDraw = true;
                return;
            }
            Element e = Hit(x, y);
            if (e == null && !edit && !S.FixedStick)
            {
                Element st = S.Find("stick");
                if (!st.Down && StickZone().Contains(x, y)) e = st;
            }
            if (e == null) return;

            touches[id] = e;
            e.Down = true; e.Touch = id;
            needDraw = true;

            if (edit)
            {
                selected = e; moved = false;
                dragDX = e.X * W - x; dragDY = e.Y * H - y;
                return;
            }

            e.CurX = x; e.CurY = y;
            switch (e.Kind)
            {
                case Kind.Stick:
                    if (S.FixedStick) { e.OriginX = e.X * W; e.OriginY = e.Y * H; }
                    else { e.OriginX = x; e.OriginY = y; }
                    UpdateStick(e);
                    break;
                case Kind.Aim:
                    aimEl = e;
                    e.OriginX = e.X * W; e.OriginY = e.Y * H;
                    PadDev.Press(e.Map);
                    UpdateAim(e);
                    break;
                case Kind.Button:
                    PadDev.Press(e.Map);
                    break;
                case Kind.Dpad:
                    UpdateDpad(e);
                    break;
                case Kind.RStick:
                    e.OriginX = e.X * W; e.OriginY = e.Y * H;
                    UpdateStick(e);
                    break;
            }
        }

        public void PointerMove(int id, float x, float y)
        {
            Element e;
            if (!touches.TryGetValue(id, out e)) return;
            if (id == resizeId)
            {
                float rx = x - e.X * W, ry = y - e.Y * H;
                float d = (float)Math.Sqrt(rx * rx + ry * ry);
                float factor = RingF(e);
                float baseR = e.R * H * S.Scale;
                e.Size = Math.Max(0.3f, Math.Min(3.5f, ((d - 6) / factor) / baseR));
                needDraw = true;
                return;
            }
            if (edit)
            {
                float nx = (x + dragDX) / W, ny = (y + dragDY) / H;
                if (Math.Abs(nx - e.X) * W + Math.Abs(ny - e.Y) * H > 4) moved = true;
                e.X = Math.Max(0.01f, Math.Min(0.99f, nx));
                e.Y = Math.Max(0.01f, Math.Min(0.99f, ny));
                needDraw = true;
                return;
            }
            e.CurX = x; e.CurY = y;
            if (e.Kind == Kind.Stick || e.Kind == Kind.RStick) UpdateStick(e);
            else if (e.Kind == Kind.Aim) UpdateAim(e);
            else if (e.Kind == Kind.Dpad) UpdateDpad(e);
            needDraw = true;
        }

        public void PointerUp(int id, float x, float y)
        {
            Element e;
            if (!touches.TryGetValue(id, out e)) return;
            touches.Remove(id);
            if (id == resizeId) { resizeId = int.MinValue; resizeEl = null; needDraw = true; return; }
            if (edit)
            {
                e.Down = false;
                if (e.Kind == Kind.Menu && !moved && OpenPanel != null) OpenPanel(this, EventArgs.Empty);
                needDraw = true;
                return;
            }
            e.CurX = x; e.CurY = y;
            EndElement(e, true);
            needDraw = true;
        }

        void EndElement(Element e, bool fromUser)
        {
            if (!e.Down) return;
            e.Down = false;
            switch (e.Kind)
            {
                case Kind.Stick:
                    PadDev.Left(0, 0);
                    break;
                case Kind.RStick:
                    PadDev.Right(0, 0);
                    break;
                case Kind.Aim:
                    FinishAim(e);
                    break;
                case Kind.Button:
                    PadDev.Release(e.Map);
                    break;
                case Kind.Dpad:
                    e.CurX = e.X * W; e.CurY = e.Y * H;
                    UpdateDpad(e);
                    break;
                case Kind.Menu:
                    {
                        float dx = e.CurX - e.X * W, dy = e.CurY - e.Y * H;
                        if (fromUser && Math.Sqrt(dx * dx + dy * dy) <= Rad(e) * 1.4 && OpenPanel != null)
                            OpenPanel(this, EventArgs.Empty);
                    }
                    break;
            }
        }

        static readonly string[] DirNames = new string[] { "Up", "Down", "Left", "Right" };

        // One D-pad icon: the part you touch decides the direction (diagonals press two).
        void UpdateDpad(Element e)
        {
            bool[] now = new bool[4];
            if (e.Down)
            {
                float dx = e.CurX - e.X * W, dy = e.CurY - e.Y * H;
                float ax = Math.Abs(dx), ay = Math.Abs(dy);
                if (Math.Sqrt(dx * dx + dy * dy) > Rad(e) * 0.2f)
                {
                    now[0] = dy < 0 && ay > ax * 0.414f;
                    now[1] = dy > 0 && ay > ax * 0.414f;
                    now[2] = dx < 0 && ax > ay * 0.414f;
                    now[3] = dx > 0 && ax > ay * 0.414f;
                }
            }
            for (int i = 0; i < 4; i++)
            {
                if (now[i] && !e.Dir[i]) PadDev.Press(DirNames[i]);
                else if (!now[i] && e.Dir[i]) PadDev.Release(DirNames[i]);
                e.Dir[i] = now[i];
            }
        }

        void UpdateStick(Element e)
        {
            float r = Rad(e);
            float dx = (e.CurX - e.OriginX) / r, dy = (e.CurY - e.OriginY) / r;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len > 1) { dx /= len; dy /= len; }
            if (e.Kind == Kind.RStick) PadDev.Right(dx, -dy); else PadDev.Left(dx, -dy);
        }

        // Big orange button: A is held while touched, dragging steers the LEFT stick.
        void UpdateAim(Element e)
        {
            float r = Rad(e);
            float dx = e.CurX - e.OriginX, dy = e.CurY - e.OriginY;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len > r * 0.15f)
            {
                float mag = Math.Min(1f, len / r);
                PadDev.Left(dx / len * mag, -dy / len * mag);
            }
            else PadDev.Left(0, 0);

            PointF c = CancelCenter();
            float cx = e.CurX - c.X, cy = e.CurY - c.Y;
            overCancel = S.CancelZone && Math.Sqrt(cx * cx + cy * cy) <= CancelRad();
        }

        void FinishAim(Element e)
        {
            string hold = e.Map, shoot = e.Map2;
            Element st = S.Find("stick");
            if (overCancel)
            {
                // Released over the CANCEL circle: stop holding A without shooting.
                PadDev.Release(hold);
                PadDev.Left(0, 0);
            }
            else
            {
                // Release = tap LT while A is still held, then let go of A.
                PadDev.Press(shoot);
                Schedule(130, delegate { PadDev.Release(shoot); });
                Schedule(200, delegate { PadDev.Release(hold); });
                Schedule(240, delegate { if (!st.Down) PadDev.Left(0, 0); });
            }
            overCancel = false; aimEl = null;
        }

        void Schedule(int ms, Action a)
        {
            sched.Add(new KeyValuePair<long, Action>(clock.ElapsedMilliseconds + ms, a));
        }

        // ---------- win32 messages ----------
        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_MOUSEACTIVATE:
                    m.Result = (IntPtr)3; // MA_NOACTIVATE
                    return;
                case Native.WM_TOUCH:
                    HandleTouch(ref m);
                    m.Result = IntPtr.Zero;
                    return;
                case Native.WM_LBUTTONDOWN:
                case Native.WM_MOUSEMOVE:
                case Native.WM_LBUTTONUP:
                    if (!FromTouch())
                    {
                        float x = (short)(m.LParam.ToInt64() & 0xFFFF);
                        float y = (short)((m.LParam.ToInt64() >> 16) & 0xFFFF);
                        if (m.Msg == Native.WM_LBUTTONDOWN) { Native.SetCapture(Handle); PointerDown(-1, x, y); }
                        else if (m.Msg == Native.WM_MOUSEMOVE) PointerMove(-1, x, y);
                        else { PointerUp(-1, x, y); Native.ReleaseCapture(); }
                    }
                    return;
            }
            base.WndProc(ref m);
        }

        static bool FromTouch()
        {
            long extra = Native.GetMessageExtraInfo().ToInt64();
            return (extra & 0xFFFFFF00L) == 0xFF515700L;
        }

        void HandleTouch(ref Message m)
        {
            int n = (int)(m.WParam.ToInt64() & 0xFFFF);
            if (n <= 0) return;
            Native.TOUCHINPUT[] ti = new Native.TOUCHINPUT[n];
            if (Native.GetTouchInputInfo(m.LParam, n, ti, Marshal.SizeOf(typeof(Native.TOUCHINPUT))))
            {
                for (int i = 0; i < n; i++)
                {
                    float x = ti[i].x / 100f - scr.Left, y = ti[i].y / 100f - scr.Top;
                    int f = ti[i].dwFlags;
                    if ((f & Native.TOUCHEVENTF_DOWN) != 0) PointerDown(ti[i].dwID, x, y);
                    else if ((f & Native.TOUCHEVENTF_UP) != 0) PointerUp(ti[i].dwID, x, y);
                    else if ((f & Native.TOUCHEVENTF_MOVE) != 0) PointerMove(ti[i].dwID, x, y);
                }
            }
            Native.CloseTouchInputHandle(m.LParam);
        }

        // ---------- loop ----------
        void OnTick(object sender, EventArgs ev)
        {
            long now = clock.ElapsedMilliseconds;
            for (int i = sched.Count - 1; i >= 0; i--)
            {
                if (sched[i].Key <= now)
                {
                    Action a = sched[i].Value;
                    sched.RemoveAt(i);
                    a();
                }
            }
            PadDev.Flush();
            if (needDraw && Visible) { needDraw = false; Render(); }
        }

        // ---------- drawing ----------
        int A(int a) { return Math.Max(2, (int)(a * S.Opacity)); }

        void Circle(float cx, float cy, float r, Brush b)
        {
            g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
        }

        void Ring(float cx, float cy, float r, Color c, float w)
        {
            using (Pen p = new Pen(c, w)) g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2);
        }

        void Render()
        {
            g.Clear(Color.Transparent);
            if (edit)
            {
                using (Brush b = new SolidBrush(Color.FromArgb(120, 0, 10, 20))) g.FillRectangle(b, 0, 0, W, H);
            }
            else if (!S.FixedStick)
            {
                RectangleF z = StickZone();
                using (Brush b = new SolidBrush(Color.FromArgb(2, 0, 0, 0))) g.FillRectangle(b, z.X, z.Y, z.Width, z.Height);
            }

            foreach (Element e in S.Items) if (Vis(e)) DrawElement(e);

            if (!edit && aimEl != null && S.CancelZone) DrawCancel();
            if (edit) DrawBanner();
            Present();
        }

        void DrawElement(Element e)
        {
            if (e.Kind == Kind.Cancel && !edit) return;
            float cx = e.X * W, cy = e.Y * H, r = Rad(e);
            if (e.Kind != Kind.Cancel)
                using (Brush hb = new SolidBrush(Color.FromArgb(2, 0, 0, 0))) Circle(cx, cy, HitR(e), hb);

            bool down = e.Down && !edit;
            switch (e.Kind)
            {
                case Kind.Stick: case Kind.RStick: DrawStick(e, cx, cy, r, down); break;
                case Kind.Aim: DrawFire(e, cx, cy, r, down); break;
                case Kind.Cancel: DrawCancelZone(cx, cy, r, false); break;
                case Kind.Dpad: DrawDpad(e, cx, cy, r); break;
                default: DrawButton(e, cx, cy, r, down); break;
            }

            if (edit)
            {
                bool sel = e == selected;
                using (Pen p = new Pen(sel ? Color.FromArgb(255, 255, 220, 60) : Color.FromArgb(200, 255, 255, 255), sel ? 3 : 1.5f))
                {
                    p.DashStyle = DashStyle.Dash;
                    float rr = EditRing(e);
                    g.DrawEllipse(p, cx - rr, cy - rr, rr * 2, rr * 2);
                }
                if (sel)
                {
                    PointF h = HandlePos(e); float hr = H * 0.016f;
                    using (Brush b = new SolidBrush(Color.FromArgb(255, 255, 220, 60))) Circle(h.X, h.Y, hr, b);
                    using (Pen p = new Pen(Color.FromArgb(255, 40, 40, 40), 2f))
                    {
                        g.DrawLine(p, h.X - hr * 0.4f, h.Y - hr * 0.4f, h.X + hr * 0.4f, h.Y + hr * 0.4f);
                    }
                }
            }
        }

        void DrawStick(Element e, float cx, float cy, float r, bool down)
        {
            float bx = cx, by = cy;
            if (down && e.Kind == Kind.Stick && !S.FixedStick) { bx = e.OriginX; by = e.OriginY; }
            bool faint = e.Kind == Kind.Stick && !S.FixedStick && !down && !edit;
            using (Brush b = new SolidBrush(Color.FromArgb(A(faint ? 40 : 80), 20, 70, 95))) Circle(bx, by, r, b);
            Ring(bx, by, r, Color.FromArgb(A(faint ? 90 : 190), 120, 220, 255), 2.5f);
            float kx = bx, ky = by;
            if (down)
            {
                float dx = e.CurX - bx, dy = e.CurY - by;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len > r) { dx = dx / len * r; dy = dy / len * r; }
                kx = bx + dx; ky = by + dy;
            }
            using (Brush b = new SolidBrush(Color.FromArgb(A(down ? 230 : 150), 140, 225, 250))) Circle(kx, ky, r * 0.38f, b);
            Ring(kx, ky, r * 0.38f, Color.FromArgb(A(220), 255, 255, 255), 2f);
            if (e.Kind == Kind.RStick) Letter("R", kx, ky, r * 0.5f, Color.FromArgb(A(255), 20, 40, 50));
        }

        void DrawFire(Element e, float cx, float cy, float r, bool down)
        {
            Color fill = down ? Color.FromArgb(A(250), 255, 190, 70) : Color.FromArgb(A(225), 240, 150, 40);
            using (Brush b = new SolidBrush(fill)) Circle(cx, cy, r, b);
            Ring(cx, cy, r, Color.FromArgb(A(255), 200, 70, 40), r * 0.09f);

            // arrow glyph
            using (Pen p = new Pen(Color.FromArgb(A(255), 70, 40, 20), r * 0.07f))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, cx - r * 0.55f, cy, cx + r * 0.5f, cy);
                g.DrawLine(p, cx + r * 0.5f, cy, cx + r * 0.25f, cy - r * 0.2f);
                g.DrawLine(p, cx + r * 0.5f, cy, cx + r * 0.25f, cy + r * 0.2f);
            }
            if (down)
            {
                // drag handle follows the finger
                float dx = e.CurX - e.OriginX, dy = e.CurY - e.OriginY;
                using (Pen p = new Pen(Color.FromArgb(A(160), 255, 255, 255), 3f))
                    g.DrawLine(p, e.OriginX, e.OriginY, e.CurX, e.CurY);
                using (Brush b = new SolidBrush(Color.FromArgb(A(230), 255, 230, 160))) Circle(e.CurX, e.CurY, r * 0.4f, b);
            }
        }

        void DrawDpad(Element e, float cx, float cy, float r)
        {
            float L = r, t = r * 0.34f;
            PointF[] pts = new PointF[] {
                new PointF(-t,-L), new PointF(t,-L), new PointF(t,-t), new PointF(L,-t), new PointF(L,t), new PointF(t,t),
                new PointF(t,L), new PointF(-t,L), new PointF(-t,t), new PointF(-L,t), new PointF(-L,-t), new PointF(-t,-t) };
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddPolygon(pts);
                Matrix m = new Matrix(); m.Translate(cx, cy); gp.Transform(m);
                using (GraphicsPath gs = (GraphicsPath)gp.Clone())
                {
                    Matrix ms = new Matrix(); ms.Translate(0, r * 0.07f); gs.Transform(ms);
                    using (Brush sb = new SolidBrush(Color.FromArgb(A(110), 0, 0, 0))) g.FillPath(sb, gs);
                }
                using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(cx - L, cy - L, L * 2, L * 2),
                    Color.FromArgb(A(240), 66, 68, 72), Color.FromArgb(A(240), 24, 25, 28), 90f)) g.FillPath(lb, gp);
                // lit arms
                RectangleF[] arms = new RectangleF[] {
                    new RectangleF(cx - t, cy - L, t * 2, L - t), new RectangleF(cx - t, cy + t, t * 2, L - t),
                    new RectangleF(cx - L, cy - t, L - t, t * 2), new RectangleF(cx + t, cy - t, L - t, t * 2) };
                for (int i = 0; i < 4; i++)
                    if (e.Dir[i] && e.Down)
                        using (Brush b = new SolidBrush(Color.FromArgb(A(200), 40, 150, 105))) g.FillRectangle(b, arms[i]);
                using (Pen p = new Pen(Color.FromArgb(A(210), 165, 170, 175), 2f)) { p.LineJoin = LineJoin.Round; g.DrawPath(p, gp); }
            }
            // arrow marks
            float s = t * 0.55f, d = (L + t) / 2;
            PointF[][] tri = new PointF[][] {
                new PointF[] { new PointF(cx, cy - d - s), new PointF(cx - s, cy - d + s * 0.7f), new PointF(cx + s, cy - d + s * 0.7f) },
                new PointF[] { new PointF(cx, cy + d + s), new PointF(cx - s, cy + d - s * 0.7f), new PointF(cx + s, cy + d - s * 0.7f) },
                new PointF[] { new PointF(cx - d - s, cy), new PointF(cx - d + s * 0.7f, cy - s), new PointF(cx - d + s * 0.7f, cy + s) },
                new PointF[] { new PointF(cx + d + s, cy), new PointF(cx + d - s * 0.7f, cy - s), new PointF(cx + d - s * 0.7f, cy + s) } };
            for (int i = 0; i < 4; i++)
                using (Brush b = new SolidBrush(e.Dir[i] && e.Down ? Color.FromArgb(A(255), 255, 255, 255) : Color.FromArgb(A(170), 190, 195, 200)))
                    g.FillPolygon(b, tri[i]);
        }

        static readonly Color Green = Color.FromArgb(61, 220, 151);

        void RoundBase(float cx, float cy, float r, bool down)
        {
            using (Brush sh = new SolidBrush(Color.FromArgb(A(110), 0, 0, 0))) Circle(cx, cy + r * 0.07f, r * 1.02f, sh);
            Color top = down ? Color.FromArgb(A(240), 40, 120, 90) : Color.FromArgb(A(240), 66, 68, 72);
            Color bot = down ? Color.FromArgb(A(240), 20, 70, 52) : Color.FromArgb(A(240), 24, 25, 28);
            using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(cx - r, cy - r, r * 2, r * 2), top, bot, 90f))
                g.FillEllipse(lb, cx - r, cy - r, r * 2, r * 2);
            if (down) Ring(cx, cy, r, Color.FromArgb(A(255), Green), 3f);
            else Ring(cx, cy, r, Color.FromArgb(A(210), 165, 170, 175), 2f);
        }

        void Hamburger(float cx, float cy, float r, Color c)
        {
            float s = r * 0.42f;
            using (Pen p = new Pen(c, Math.Max(2f, r * 0.1f)))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                for (int i = -1; i <= 1; i++) g.DrawLine(p, cx - s, cy + i * s * 0.62f, cx + s, cy + i * s * 0.62f);
            }
        }

        void DpadGlyph(float cx, float cy, float r, string dir)
        {
            float s = r * 0.55f, t = s * 0.34f;
            Color dim = Color.FromArgb(A(150), 150, 155, 160), hi = Color.FromArgb(A(255), 255, 255, 255);
            using (Brush bd = new SolidBrush(dim)) using (Brush bh = new SolidBrush(hi))
            {
                g.FillRectangle(bd, cx - t, cy - s, t * 2, s * 2);
                g.FillRectangle(bd, cx - s, cy - t, s * 2, t * 2);
                if (dir == "Up") g.FillRectangle(bh, cx - t, cy - s, t * 2, s);
                else if (dir == "Down") g.FillRectangle(bh, cx - t, cy, t * 2, s);
                else if (dir == "Left") g.FillRectangle(bh, cx - s, cy - t, s, t * 2);
                else g.FillRectangle(bh, cx, cy - t, s, t * 2);
            }
        }

        void DrawShoulder(string map, float cx, float cy, float r, bool down)
        {
            bool trig = map.EndsWith("T");
            float w = r * 2.1f, h = r * (trig ? 1.2f : 0.85f);
            RectangleF rc = new RectangleF(cx - w / 2, cy - h / 2, w, h);
            using (GraphicsPath gp = RoundRect(rc, h * (trig ? 0.32f : 0.45f)))
            {
                RectangleF sh = rc; sh.Y += r * 0.07f;
                using (GraphicsPath gs = RoundRect(sh, h * (trig ? 0.32f : 0.45f)))
                using (Brush sb = new SolidBrush(Color.FromArgb(A(110), 0, 0, 0))) g.FillPath(sb, gs);
                Color top = down ? Color.FromArgb(A(240), 40, 120, 90) : Color.FromArgb(A(240), 70, 72, 76);
                Color bot = down ? Color.FromArgb(A(240), 20, 70, 52) : Color.FromArgb(A(240), 26, 27, 30);
                using (LinearGradientBrush lb = new LinearGradientBrush(rc, top, bot, 90f)) g.FillPath(lb, gp);
                using (Pen p = new Pen(down ? Color.FromArgb(A(255), Green) : Color.FromArgb(A(210), 165, 170, 175), down ? 3f : 2f)) g.DrawPath(p, gp);
            }
            Letter(map, cx, cy, h * 1.25f, Color.FromArgb(A(255), 255, 255, 255));
        }

        void DrawButton(Element e, float cx, float cy, float r, bool down)
        {
            string map = e.Map;
            if (e.Kind != Kind.Menu && (map == "LB" || map == "RB" || map == "LT" || map == "RT"))
            {
                DrawShoulder(map, cx, cy, r, down);
                return;
            }
            RoundBase(cx, cy, r, down);
            Color white = Color.FromArgb(A(255), 255, 255, 255);
            if (e.Kind == Kind.Menu) { Ring(cx, cy, r, Color.FromArgb(A(255), Green), 2.5f); Hamburger(cx, cy, r, Color.FromArgb(A(255), Green)); return; }
            switch (map)
            {
                case "A": case "B": case "X": case "Y":
                    Letter(map, cx, cy, r * 1.35f, white); return;
                case "L3": case "R3":
                    Letter(map.Substring(0, 1), cx, cy, r * 1.2f, white); return;
                case "Start":
                    Hamburger(cx, cy, r, white); return;
                case "Back":
                    {
                        float s = r * 0.3f;
                        using (Pen p = new Pen(white, Math.Max(2f, r * 0.09f)))
                        {
                            g.DrawRectangle(p, cx - s * 1.4f, cy - s * 0.3f, s * 1.6f, s * 1.2f);
                            g.DrawRectangle(p, cx - s * 0.2f, cy - s * 0.9f, s * 1.6f, s * 1.2f);
                        }
                        return;
                    }
                case "Up": case "Down": case "Left": case "Right":
                    DpadGlyph(cx, cy, r, map); return;
            }
            Letter("-", cx, cy, r * 1.2f, white);
        }

        // Xbox 360 style glyphs for the on-screen buttons.


        void Letter(string t, float cx, float cy, float size, Color c)
        {
            using (Font f = new Font("Segoe UI", size * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush b = new SolidBrush(c))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                g.DrawString(t, f, b, new RectangleF(cx - size, cy - size, size * 2, size * 2), sf);
            }
        }

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void DrawCancel()
        {
            PointF c = CancelCenter();
            DrawCancelZone(c.X, c.Y, CancelRad(), overCancel);
        }

        void DrawCancelZone(float cx, float cy, float r, bool active)
        {
            using (Brush b = new SolidBrush(Color.FromArgb(A(active ? 190 : 120), active ? 190 : 20, active ? 50 : 24, active ? 50 : 28))) Circle(cx, cy, r, b);
            Ring(cx, cy, r, Color.FromArgb(A(230), active ? 255 : 165, active ? 120 : 170, active ? 120 : 175), 3f);
            using (Font f = new Font("Segoe UI", r * 0.2f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush tb = new SolidBrush(Color.FromArgb(A(255), 255, 255, 255)))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                g.DrawString("CANCEL", f, tb, new RectangleF(cx - r, cy - r, r * 2, r * 2), sf);
            }
        }

        void DrawBanner()
        {
            string t = "EDIT MODE  -  drag to move, tap a button then drag its yellow handle to resize.  Menu button = settings, then Save.";
            using (Font f = new Font("Segoe UI", H * 0.022f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush tb = new SolidBrush(Color.White))
            using (Brush bb = new SolidBrush(Color.FromArgb(200, 10, 40, 60)))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                SizeF sz = g.MeasureString(t, f);
                RectangleF rc = new RectangleF(W / 2 - sz.Width / 2 - 20, H * 0.12f - sz.Height / 2 - 8, sz.Width + 40, sz.Height + 16);
                g.FillRectangle(bb, rc);
                g.DrawString(t, f, tb, rc, sf);
            }
        }

        void Present()
        {
            if (!IsHandleCreated) return;
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hBmp = bmp.GetHbitmap(Color.FromArgb(0));
                old = Native.SelectObject(memDc, hBmp);
                Native.SIZE size; size.cx = bmp.Width; size.cy = bmp.Height;
                Native.POINT src; src.x = 0; src.y = 0;
                Native.POINT top; top.x = scr.Left; top.y = scr.Top;
                Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION();
                bf.BlendOp = 0; bf.BlendFlags = 0; bf.SourceConstantAlpha = 255; bf.AlphaFormat = 1;
                Native.UpdateLayeredWindow(Handle, screenDc, ref top, ref size, memDc, ref src, 0, ref bf, 2);
            }
            finally
            {
                if (old != IntPtr.Zero) Native.SelectObject(memDc, old);
                if (hBmp != IntPtr.Zero) Native.DeleteObject(hBmp);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) needDraw = true; else ReleaseTouches();
        }
    }
}
