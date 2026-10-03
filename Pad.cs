// DiverTouch - virtual Xbox 360 pad wrapper (ViGEmBus). C# 5 compatible.
using System;
using System.Collections.Generic;
using System.Reflection;

namespace DiverTouch
{
    public class Pad
    {
        public bool Ok;
        public string Error = "";

        object client;
        object pad;
        Type tPad, tButton, tAxis, tSlider;
        MethodInfo mSetButton, mSetAxis, mSetSlider, mSubmit;
        Dictionary<string, object> buttons = new Dictionary<string, object>();
        Dictionary<string, int> held = new Dictionary<string, int>();
        short lx, ly, rx, ry;
        byte lt, rt;
        object axLX, axLY, axRX, axRY, slLT, slRT;
        public bool Dirty;

        // ViGEm is loaded by reflection so the app also builds with the old
        // csc.exe that ships with Windows (no extra SDK needed).
        public void Init(string dllPath)
        {
            try
            {
                Assembly asm = Assembly.LoadFrom(dllPath);
                Type tClient = asm.GetType("Nefarius.ViGEm.Client.ViGEmClient", true);
                tButton = asm.GetType("Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button", true);
                tAxis = asm.GetType("Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Axis", true);
                tSlider = asm.GetType("Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Slider", true);
                tPad = asm.GetType("Nefarius.ViGEm.Client.Targets.IXbox360Controller", true);
                Type tVirt = asm.GetType("Nefarius.ViGEm.Client.IVirtualGamepad", true);

                client = Activator.CreateInstance(tClient);
                pad = tClient.GetMethod("CreateXbox360Controller", Type.EmptyTypes).Invoke(client, null);

                tVirt.GetProperty("AutoSubmitReport").SetValue(pad, false, null);
                mSetButton = tPad.GetMethod("SetButtonState", new Type[] { tButton, typeof(bool) });
                mSetAxis = tPad.GetMethod("SetAxisValue", new Type[] { tAxis, typeof(short) });
                mSetSlider = tPad.GetMethod("SetSliderValue", new Type[] { tSlider, typeof(byte) });
                mSubmit = tVirt.GetMethod("SubmitReport");

                Map("A", "A"); Map("B", "B"); Map("X", "X"); Map("Y", "Y");
                Map("LB", "LeftShoulder"); Map("RB", "RightShoulder");
                Map("Start", "Start"); Map("Back", "Back");
                Map("L3", "LeftThumb"); Map("R3", "RightThumb");
                Map("Up", "Up"); Map("Down", "Down"); Map("Left", "Left"); Map("Right", "Right");

                axLX = Static(tAxis, "LeftThumbX"); axLY = Static(tAxis, "LeftThumbY");
                axRX = Static(tAxis, "RightThumbX"); axRY = Static(tAxis, "RightThumbY");
                slLT = Static(tSlider, "LeftTrigger"); slRT = Static(tSlider, "RightTrigger");

                tVirt.GetMethod("Connect", Type.EmptyTypes).Invoke(pad, null);
                Ok = true;
                Dirty = true;
                Flush();
            }
            catch (Exception ex)
            {
                Exception e = ex;
                while (e.InnerException != null) e = e.InnerException;
                Error = e.Message;
                Ok = false;
            }
        }

        void Map(string name, string field) { buttons[name] = Static(tButton, field); }

        static object Static(Type t, string name)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (f != null) return f.GetValue(null);
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (p != null) return p.GetValue(null, null);
            throw new Exception("ViGEm member not found: " + t.Name + "." + name);
        }

        // Several on-screen buttons can map to the same pad button, so count presses.
        public void Press(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "None") return;
            int n; held.TryGetValue(name, out n);
            held[name] = n + 1;
            Dirty = true;
        }

        public void Release(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "None") return;
            int n; held.TryGetValue(name, out n);
            held[name] = Math.Max(0, n - 1);
            Dirty = true;
        }

        public void Left(float x, float y) { lx = Conv(x); ly = Conv(y); Dirty = true; }
        public void Right(float x, float y) { rx = Conv(x); ry = Conv(y); Dirty = true; }
        static short Conv(float v) { if (v > 1) v = 1; if (v < -1) v = -1; return (short)(v * 32767f); }

        public void ReleaseAll()
        {
            held.Clear(); lx = ly = rx = ry = 0; Dirty = true; Flush();
        }

        public void Flush()
        {
            if (!Ok || !Dirty) return;
            Dirty = false;
            try
            {
                foreach (KeyValuePair<string, object> kv in buttons)
                {
                    int n; held.TryGetValue(kv.Key, out n);
                    mSetButton.Invoke(pad, new object[] { kv.Value, n > 0 });
                }
                int t; held.TryGetValue("LT", out t); lt = (byte)(t > 0 ? 255 : 0);
                held.TryGetValue("RT", out t); rt = (byte)(t > 0 ? 255 : 0);
                mSetSlider.Invoke(pad, new object[] { slLT, lt });
                mSetSlider.Invoke(pad, new object[] { slRT, rt });
                mSetAxis.Invoke(pad, new object[] { axLX, lx });
                mSetAxis.Invoke(pad, new object[] { axLY, ly });
                mSetAxis.Invoke(pad, new object[] { axRX, rx });
                mSetAxis.Invoke(pad, new object[] { axRY, ry });
                mSubmit.Invoke(pad, null);
            }
            catch (Exception ex)
            {
                Exception e = ex;
                while (e.InnerException != null) e = e.InnerException;
                Error = e.Message; Ok = false;
            }
        }

        public void Dispose()
        {
            try
            {
                if (pad != null) { ReleaseAll(); }
                IDisposable d = client as IDisposable;
                if (d != null) d.Dispose();
            }
            catch { }
        }
    }
}
