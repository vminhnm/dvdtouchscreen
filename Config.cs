// DiverTouch - layout + settings. C# 5 compatible.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DiverTouch
{
    public enum Kind { Stick, Aim, Button, Menu, Cancel, Dpad, RStick }

    public class Element
    {
        public string Id, Label;
        public Kind Kind;
        public float X, Y;      // centre, fraction of screen width / height
        public float Size = 1f;  // user resize multiplier
        public float R;         // radius, fraction of screen height (at 100% size)
        public string Map;      // pad button name
        public string Map2, DefMap2; // Aim only: button pressed on release (fires)
        public string Title;    // name shown in the mapping list
        public bool Dpad;       // belongs to the optional D-pad group
        public float DefX, DefY;
        public string DefMap;

        // runtime state
        public bool Down;
        public bool[] Dir = new bool[4]; // d-pad: up, down, left, right
        public int Touch;
        public float OriginX, OriginY, CurX, CurY; // px

        public Element(string id, string label, Kind k, float x, float y, float r, string map)
        {
            Id = id; Label = label; Kind = k; X = DefX = x; Y = DefY = y; R = r; Map = DefMap = map;
        }
    }

    public class Settings
    {
        public float Scale = 1.0f;       // button size
        public float Opacity = 0.85f;
        public bool FixedStick = true;   // like "Fix left stick" in the mobile game
        public bool ShowDpad = true;
        public bool CancelZone = true;
        public string CancelButton = "None";
        public int Monitor = 0;
        public List<Element> Items = new List<Element>();

        public static readonly string[] PadButtons = new string[] {
            "None","A","B","X","Y","LB","RB","LT","RT","Start","Back","L3","R3","Up","Down","Left","Right" };

        public Settings() { BuildDefaults(); }

        // Positions are traced from the mobile Dave the Diver layout (landscape).
        void BuildDefaults()
        {
            Items.Clear();
            Items.Add(new Element("stick", "Move", Kind.Stick, 0.140f, 0.721f, 0.115f, "None"));
            Element f = new Element("fire", "Harpoon", Kind.Aim, 0.862f, 0.800f, 0.0875f, "A"); f.Map2 = f.DefMap2 = "RT"; Items.Add(f);
            Items.Add(new Element("rstick", "Look", Kind.RStick, 0.790f, 0.547f, 0.050f, "None"));
            // Buttons are fixed: each one sends the Xbox button it shows.
            float fr = 0.0504f; // A B X Y share one size
            Btn("A", "A", 0.7126f, 0.674f, fr, "A");
            Btn("B", "B", 0.804f, 0.680f, fr, "B");
            Btn("X", "X", 0.7576f, 0.779f, fr, "X");
            Btn("Y", "Y", 0.961f, 0.887f, fr, "Y");
            Btn("LB", "LB", 0.670f, 0.927f, 0.047f, "LB");   // LB RB share one size
            Btn("RB", "RB", 0.753f, 0.925f, 0.047f, "RB");
            Btn("RT", "RT", 0.879f, 0.531f, 0.0517f, "RT"); // LT RT share one size
            Btn("LT", "LT", 0.884f, 0.652f, 0.0517f, "LT");
            Items.Add(new Element("dpad", "D-pad", Kind.Dpad, 0.310f, 0.825f, 0.0663f, "None"));
            Btn("start", "Menu", 0.810f, 0.051f, 0.031f, "Start");
            Items.Add(new Element("menu", "\u2630", Kind.Menu, 0.872f, 0.051f, 0.031f, "None"));
            Btn("back", "View", 0.935f, 0.051f, 0.031f, "Back");
            Items.Add(new Element("cancel", "Cancel zone", Kind.Cancel, 0.968f, 0.666f, 0.038f, "None"));
        }

        Element Btn(string id, string title, float x, float y, float r, string map)
        {
            Element e = new Element(id, map, Kind.Button, x, y, r, map);
            e.Title = title; Items.Add(e); return e;
        }

        public void ResetLayout()
        {
            foreach (Element e in Items) { e.X = e.DefX; e.Y = e.DefY; e.Size = 1f; }
        }

        public void ResetMapping()
        {
            foreach (Element e in Items) { e.Map = e.DefMap; e.Map2 = e.DefMap2; }
        }

        public Element Find(string id)
        {
            foreach (Element e in Items) if (e.Id == id) return e;
            return null;
        }

        public static string Dir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dvdtouchscreen");
            }
        }
        static string FilePath { get { return Path.Combine(Dir, "config.ini"); } }

        static string F(float v) { return v.ToString("0.####", CultureInfo.InvariantCulture); }
        static float P(string s, float def)
        {
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                List<string> lines = new List<string>();
                lines.Add("ver=6");
                lines.Add("scale=" + F(Scale));
                lines.Add("opacity=" + F(Opacity));
                lines.Add("fixed=" + (FixedStick ? "1" : "0"));
                lines.Add("dpad=" + (ShowDpad ? "1" : "0"));
                lines.Add("cancel=" + (CancelZone ? "1" : "0"));
                lines.Add("cancelbtn=" + CancelButton);
                lines.Add("monitor=" + Monitor);
                foreach (Element e in Items)
                {
                    lines.Add("pos." + e.Id + "=" + F(e.X) + "," + F(e.Y));
                    lines.Add("size." + e.Id + "=" + F(e.Size));
                }
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch { }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                string[] all = File.ReadAllLines(FilePath);
                bool current = Array.IndexOf(all, "ver=6") >= 0; // older layouts are dropped
                foreach (string raw in all)
                {
                    int i = raw.IndexOf('=');
                    if (i < 0) continue;
                    string k = raw.Substring(0, i).Trim(), v = raw.Substring(i + 1).Trim();
                    if (k == "scale") s.Scale = Math.Max(0.4f, Math.Min(2f, P(v, 1f)));
                    else if (k == "opacity") s.Opacity = Math.Max(0.15f, Math.Min(1f, P(v, 0.85f)));
                    else if (k == "fixed") s.FixedStick = v == "1";
                    else if (k == "dpad") s.ShowDpad = v == "1";
                    else if (k == "cancel") s.CancelZone = v == "1";
                    else if (k == "cancelbtn") s.CancelButton = v;
                    else if (k == "monitor") { int m; if (int.TryParse(v, out m)) s.Monitor = m; }
                    else if (!current) { }
                    else if (k.StartsWith("pos."))
                    {
                        Element e = s.Find(k.Substring(4));
                        string[] p = v.Split(',');
                        if (e != null && p.Length == 2)
                        {
                            e.X = Math.Max(0f, Math.Min(1f, P(p[0], e.X)));
                            e.Y = Math.Max(0f, Math.Min(1f, P(p[1], e.Y)));
                        }
                    }
                    else if (k.StartsWith("size."))
                    {
                        Element e = s.Find(k.Substring(5));
                        if (e != null) e.Size = Math.Max(0.3f, Math.Min(3.5f, P(v, 1f)));
                    }
                }
            }
            catch { }
            return s;
        }
    }
}
