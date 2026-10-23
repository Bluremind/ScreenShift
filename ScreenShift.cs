// Required Notice: Copyright (c) 2026 Bluremind (https://github.com/Bluremind/ScreenShift)
// Free for personal use under the ScreenShift Personal Use License (see LICENSE). Any other use needs a paid license (COMMERCIAL.md).
//
// ScreenShift: moves the Windows picture off the dead strips of a damaged panel.
// The desktop is placed inside the part of the panel that still shows an image
// (Windows 10 virtual modes via SetDisplayConfig); the dead strips stay black.
//
//   ScreenShift.exe          calibrate with the arrow keys, Enter applies (auto-revert in 20 s unless kept)
//   ScreenShift.exe --reset  remove the shift
//   ScreenShift.exe --apply  re-apply the saved shift without asking
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenShift
{
    public static class Native
    {
        [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
        [DllImport("user32.dll")] public static extern int QueryDisplayConfig(uint flags, ref uint numPaths, byte[] paths, ref uint numModes, byte[] modes, IntPtr topology);
        [DllImport("user32.dll")] public static extern int SetDisplayConfig(uint numPaths, byte[] paths, uint numModes, byte[] modes, uint flags);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettingsEx(string device, int modeNum, ref DEVMODE mode, int flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
    }

    // Raw CCD configuration of the single active display (virtual-mode aware layout).
    public class DisplayConfig
    {
        const uint QDC_ONLY_ACTIVE_PATHS = 0x2, QDC_VIRTUAL_MODE_AWARE = 0x10;
        public const uint SDC_USE_DATABASE_CURRENT = 0xF, SDC_USE_SUPPLIED = 0x20, SDC_VALIDATE = 0x40,
            SDC_APPLY = 0x80, SDC_SAVE_TO_DATABASE = 0x200, SDC_VIRTUAL_MODE_AWARE = 0x8000;
        const int PathSize = 72, ModeSize = 64;
        const int SourceMode = 1, TargetMode = 2, DesktopImage = 3;

        byte[] paths, modes;
        uint numPaths, numModes;
        int src = -1, tgt = -1, img = -1;

        public static DisplayConfig Query()
        {
            const uint flags = QDC_ONLY_ACTIVE_PATHS | QDC_VIRTUAL_MODE_AWARE;
            uint np, nm;
            Check(Native.GetDisplayConfigBufferSizes(flags, out np, out nm), "GetDisplayConfigBufferSizes");
            var c = new DisplayConfig { paths = new byte[np * PathSize], modes = new byte[nm * ModeSize] };
            Check(Native.QueryDisplayConfig(flags, ref np, c.paths, ref nm, c.modes, IntPtr.Zero), "QueryDisplayConfig");
            c.numPaths = np;
            c.numModes = nm;
            for (int i = 0; i < nm; i++)
            {
                int type = c.Get(i * ModeSize);
                if (type == SourceMode && c.src < 0) c.src = i;
                if (type == TargetMode && c.tgt < 0) c.tgt = i;
                if (type == DesktopImage && c.img < 0) c.img = i;
            }
            if (np != 1 || c.src < 0 || c.tgt < 0 || c.img < 0)
                throw new Exception(L.F("single", np));
            return c;
        }

        int Get(int offset) { return BitConverter.ToInt32(modes, offset); }
        void Set(int offset, int value) { BitConverter.GetBytes(value).CopyTo(modes, offset); }

        public uint Rotation { get { return BitConverter.ToUInt32(paths, 40); } }
        public Size SignalSize { get { return new Size(Get(tgt * ModeSize + 40), Get(tgt * ModeSize + 44)); } }
        // Target timings stay in physical orientation; public geometry follows the visible desktop.
        public Size Panel
        {
            get { Size s = SignalSize; return Rotation == 2 || Rotation == 4 ? new Size(s.Height, s.Width) : s; }
        }
        Size OrientedSize(Size size) { return Rotation == 2 || Rotation == 4 ? new Size(size.Height, size.Width) : size; }
        public Size Desktop { get { return OrientedSize(new Size(Get(src * ModeSize + 16), Get(src * ModeSize + 20))); } }
        public Size PathSource { get { return OrientedSize(new Size(Get(img * ModeSize + 16), Get(img * ModeSize + 20))); } }
        public Rectangle Region
        {
            get {
                int o = img * ModeSize + 24;
                Rectangle r = Rectangle.FromLTRB(Get(o), Get(o + 4), Get(o + 8), Get(o + 12));
                Size s = new Size(Get(img * ModeSize + 16), Get(img * ModeSize + 20));
                if (Rotation == 2) return new Rectangle(s.Height - r.Bottom, r.Left, r.Height, r.Width);
                if (Rotation == 3) return new Rectangle(s.Width - r.Right, s.Height - r.Bottom, r.Width, r.Height);
                if (Rotation == 4) return new Rectangle(r.Top, s.Width - r.Right, r.Height, r.Width);
                return r;
            }
        }
        public bool IsIdentity
        {
            get { return Desktop == Panel && PathSource == Panel && Region == new Rectangle(Point.Empty, Panel); }
        }

        // Same topology, desktop of the given size shown in `region` of the panel (scaled if the sizes differ).
        public DisplayConfig With(Size desktop, Rectangle region)
        {
            var c = new DisplayConfig
            {
                paths = (byte[])paths.Clone(), modes = (byte[])modes.Clone(),
                numPaths = numPaths, numModes = numModes, src = src, tgt = tgt, img = img,
            };
            // CCD stores the surface and image region before the path rotation.
            // Public geometry and the overlay use the user's visible, rotated desktop.
            Size panel = SignalSize;
            desktop = OrientedSize(desktop);
            if (Rotation == 2) region = new Rectangle(region.Top, panel.Height - region.Right, region.Height, region.Width);
            else if (Rotation == 3) region = new Rectangle(panel.Width - region.Right, panel.Height - region.Bottom, region.Width, region.Height);
            else if (Rotation == 4) region = new Rectangle(panel.Width - region.Bottom, region.Left, region.Height, region.Width);
            c.Set(src * ModeSize + 16, desktop.Width);
            c.Set(src * ModeSize + 20, desktop.Height);
            int o = img * ModeSize + 16;
            c.Set(o, panel.Width);
            c.Set(o + 4, panel.Height);
            c.Set(o + 8, region.Left);
            c.Set(o + 12, region.Top);
            c.Set(o + 16, region.Right);
            c.Set(o + 20, region.Bottom);
            c.Set(o + 24, 0);
            c.Set(o + 28, 0);
            c.Set(o + 32, desktop.Width);
            c.Set(o + 36, desktop.Height);
            return c;
        }

        public DisplayConfig Identity() { return With(Panel, new Rectangle(Point.Empty, Panel)); }

        public int Apply(uint flags)
        {
            return Native.SetDisplayConfig(numPaths, paths, numModes, modes, SDC_USE_SUPPLIED | SDC_VIRTUAL_MODE_AWARE | flags);
        }

        public static int ApplyDatabase()
        {
            return Native.SetDisplayConfig(0, null, 0, null, SDC_APPLY | SDC_USE_DATABASE_CURRENT);
        }

        public static void Check(int rc, string what)
        {
            if (rc != 0) throw new Exception(what + L.T(": код ошибки ", ": error code ") + rc + ".");
        }
    }

    public static class Planner
    {
        static List<Size> crispModes;
        static List<Size> driverModes;
        public static List<Size> DriverModes()
        {
            if (driverModes != null) return driverModes;
            var list = new List<Size>();
            var dm = new Native.DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(Native.DEVMODE));
            for (int i = 0; Native.EnumDisplaySettingsEx(null, i, ref dm, 0); i++)
            {
                Size s = new Size(dm.dmPelsWidth, dm.dmPelsHeight);
                if (s.Width > 0 && s.Height > 0 && s.Width <= 8192 && s.Height <= 8192 && !list.Contains(s)) list.Add(s);
            }
            list.Sort((a, b) => a.Width != b.Width ? a.Width.CompareTo(b.Width) : a.Height.CompareTo(b.Height));
            return driverModes = list;
        }
        // Packed explicit driver dimensions are distinct from custom width-only selections.
        public static int DriverKey(Size size) { return size.Width * 10000 + size.Height; }
        public static bool ValidResolutionKey(int key)
        {
            return key >= 0 && (key <= 8192 || (key / 10000 >= 1 && key / 10000 <= 8192 && key % 10000 >= 1 && key % 10000 <= 8192));
        }
        public static Rectangle Fit(Size desktop, Rectangle area)
        {
            double scale = Math.Min((double)area.Width / desktop.Width, (double)area.Height / desktop.Height);
            int w = Math.Max(1, Math.Min(area.Width, (int)Math.Round(desktop.Width * scale)));
            int h = Math.Max(1, Math.Min(area.Height, (int)Math.Round(desktop.Height * scale)));
            return new Rectangle(area.Left + (area.Width - w) / 2, area.Top + (area.Height - h) / 2, w, h);
        }
        public static List<Size> CompatibleCandidates(Size desired, Rectangle area, IEnumerable<Size> modes)
        {
            var list = NearestAspectModes(area.Size, modes);
            double aspect = (double)area.Width / area.Height;
            list.Sort(delegate(Size a, Size b) {
                double sa = Math.Abs(Math.Log(((double)a.Width / a.Height) / aspect)) * 10 + Math.Abs(Math.Log((double)a.Width / desired.Width));
                double sb = Math.Abs(Math.Log(((double)b.Width / b.Height) / aspect)) * 10 + Math.Abs(Math.Log((double)b.Width / desired.Width));
                return sa.CompareTo(sb);
            });
            return list;
        }
        public static List<Size> ValidatedNearestModes(Rectangle area, IEnumerable<Size> modes, Func<Size, Rectangle, int> validate)
        {
            var accepted = new List<Size>();
            if (validate == null || area.Width <= 0 || area.Height <= 0) return accepted;
            foreach (Size mode in modes)
                if (mode.Width > 0 && mode.Height > 0 && !accepted.Contains(mode) && validate(mode, Fit(mode, area)) == 0)
                    accepted.Add(mode);
            return NearestAspectModes(area.Size, accepted);
        }
        public static string AspectLabel(Size mode)
        {
            if (mode.Width <= 0 || mode.Height <= 0) return "?";
            int a = mode.Width, b = mode.Height;
            while (b != 0) { int remainder = a % b; a = b; b = remainder; }
            string exact = (mode.Width / a) + ":" + (mode.Height / a);
            if (mode.Width / a <= 64 && mode.Height / a <= 64) return exact;
            int[,] common = { {1,1}, {4,3}, {5,3}, {5,4}, {16,9}, {16,10}, {64,27}, {43,18}, {32,9},
                {3,4}, {3,5}, {4,5}, {9,16}, {10,16}, {27,64}, {18,43}, {9,32} };
            for (int i = 0; i < common.GetLength(0); i++)
                if (Math.Abs(Math.Log(((double)mode.Width / mode.Height) / ((double)common[i,0] / common[i,1]))) <= 0.002)
                    return "≈" + common[i,0] + ":" + common[i,1];
            return exact;
        }
        public static List<Size> NearestAspectModes(Size area, IEnumerable<Size> modes)
        {
            var result = new List<Size>();
            if (area.Width <= 0 || area.Height <= 0) return result;
            double ratio = (double)area.Width / area.Height, best = double.MaxValue;
            foreach (Size mode in modes)
            {
                if (mode.Width <= 0 || mode.Height <= 0) continue;
                best = Math.Min(best, Math.Abs(Math.Log(((double)mode.Width / mode.Height) / ratio)));
            }
            // 0.2% tolerance groups integer-rounded variants such as 1366x768 with 16:9.
            foreach (Size mode in modes)
                if (mode.Width > 0 && mode.Height > 0 &&
                    Math.Abs(Math.Log(((double)mode.Width / mode.Height) / ratio)) <= best + 0.002 && !result.Contains(mode))
                    result.Add(mode);
            return result;
        }

        // Standard modes with the panel's aspect ratio, largest first; these can be shown 1:1 without blur.
        public static List<Size> CrispModes(Size panel)
        {
            if (crispModes != null) return crispModes;
            var list = new List<Size>();
            var dm = new Native.DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(Native.DEVMODE));
            for (int i = 0; Native.EnumDisplaySettingsEx(null, i, ref dm, 0); i++)
            {
                var s = new Size(dm.dmPelsWidth, dm.dmPelsHeight);
                if ((long)s.Width * panel.Height == (long)s.Height * panel.Width && s.Width <= panel.Width && !list.Contains(s))
                    list.Add(s);
            }
            list.Sort((a, b) => b.Width.CompareTo(a.Width));
            return crispModes = list;
        }

        // Cuts are measured inward from each physical panel edge.
        public static bool Plan(Size panel, int cutLeft, int cutTop, int cutRight, int cutBottom, bool crisp, out Size desktop, out Rectangle region)
        {
            return Plan(panel, cutLeft, cutTop, cutRight, cutBottom, crisp, true, out desktop, out region);
        }

        public static bool Plan(Size panel, int cutLeft, int cutTop, int cutRight, int cutBottom, bool crisp, bool keepAspect, out Size desktop, out Rectangle region)
        {
            return Plan(panel, cutLeft, cutTop, cutRight, cutBottom, crisp, keepAspect, false, 0, out desktop, out region);
        }

        // Integer pixel dimensions approximate the selected aspect ratio to the nearest pixel.
        public static Size AreaResolution(Size area, int width)
        {
            if (area.Width <= 0 || area.Height <= 0 || !ValidResolutionKey(width)) return Size.Empty;
            if (width > 8192) return new Size(width / 10000, width % 10000);
            if (width == 0) return area;
            int height = (int)Math.Round((double)width * area.Height / area.Width);
            return height < 1 || height > 8192 ? Size.Empty : new Size(width, height);
        }

        public static bool Plan(Size panel, int cutLeft, int cutTop, int cutRight, int cutBottom, bool crisp, bool keepAspect, bool matchArea, int resolutionWidth, out Size desktop, out Rectangle region)
        {
            desktop = Size.Empty;
            region = Rectangle.Empty;
            if (panel.Width <= 0 || panel.Height <= 0 || cutLeft < 0 || cutTop < 0 || cutRight < 0 || cutBottom < 0 ||
                (long)cutLeft + cutRight >= panel.Width || (long)cutTop + cutBottom >= panel.Height) return false;
            int vw = panel.Width - cutLeft - cutRight, vh = panel.Height - cutTop - cutBottom;
            if (matchArea)
            {
                desktop = AreaResolution(new Size(vw, vh), resolutionWidth);
                if (desktop.IsEmpty) return false;
                region = resolutionWidth > 8192 ? Fit(desktop, new Rectangle(cutLeft, cutTop, vw, vh)) : new Rectangle(cutLeft, cutTop, vw, vh);
                return true;
            }
            if (crisp)
            {
                foreach (Size m in CrispModes(panel))
                {
                    if (m.Width > vw || m.Height > vh) continue;
                    desktop = m;
                    region = new Rectangle(cutLeft + (vw - m.Width) / 2, cutTop + (vh - m.Height) / 2, m.Width, m.Height);
                    return true;
                }
                desktop = Size.Empty;
                region = Rectangle.Empty;
                return false;
            }
            if (!keepAspect)
            {
                desktop = panel;
                region = new Rectangle(cutLeft, cutTop, vw, vh);
                return true;
            }
            double s = Math.Min((double)vw / panel.Width, (double)vh / panel.Height);
            int rw = (int)Math.Round(panel.Width * s), rh = (int)Math.Round(panel.Height * s);
            if (rw < 1 || rh < 1) return false;
            desktop = panel;
            region = new Rectangle(cutLeft + (vw - rw) / 2, cutTop + (vh - rh) / 2, rw, rh);
            return true;
        }
    }

    public class Overlay : Form
    {
        public int CutLeft, CutTop, CutRight, CutBottom;
        public bool Crisp;
        readonly CheckBox aspectBox;
        readonly CheckBox resolutionBox;
        readonly ComboBox resolutionList;
        readonly ComboBox languageList;
        readonly Label languageLabel;
        readonly Func<Size, Rectangle, int> validateMode;
        Rectangle validatedArea;
        List<Size> validatedModes;
        bool updatingResolutions;
        int resolutionWidth;
        public bool MatchArea { get { return resolutionBox.Checked; } }
        public int ResolutionWidth { get { return resolutionWidth; } }
        public bool KeepAspect { get { return aspectBox.Checked; } }
        readonly Size panel;

        public Overlay(Size panel, int cutLeft, int cutTop, int cutRight, int cutBottom, bool crisp)
            : this(panel, cutLeft, cutTop, cutRight, cutBottom, crisp, true) { }

        public Overlay(Size panel, int cutLeft, int cutTop, int cutRight, int cutBottom, bool crisp, bool keepAspect)
            : this(panel, cutLeft, cutTop, cutRight, cutBottom, crisp, keepAspect, false, 0) { }

        public Overlay(Size panel, int cutLeft, int cutTop, int cutRight, int cutBottom, bool crisp, bool keepAspect, bool matchArea, int resolutionWidth)
            : this(panel, cutLeft, cutTop, cutRight, cutBottom, crisp, keepAspect, matchArea, resolutionWidth, null) { }

        public Overlay(Size panel, int cutLeft, int cutTop, int cutRight, int cutBottom, bool crisp, bool keepAspect, bool matchArea, int resolutionWidth, Func<Size, Rectangle, int> validateMode)
        {
            this.validateMode = validateMode;
            this.panel = panel;
            CutLeft = cutLeft;
            CutTop = cutTop;
            CutRight = cutRight;
            CutBottom = cutBottom;
            ClampCuts();
            Crisp = crisp;
            Text = L.Caption;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(Point.Empty, panel);
            TopMost = true;
            BackColor = Color.Black;
            DoubleBuffered = true;
            aspectBox = new CheckBox {
                Text = L.Get("aspect"),
                Checked = keepAspect, ForeColor = Color.White, BackColor = Color.FromArgb(24, 24, 24),
                AutoSize = false, Font = new Font("Segoe UI", 16f, GraphicsUnit.Pixel), TabStop = true,
                AccessibleName = "Keep original aspect ratio"
            };
            aspectBox.CheckedChanged += delegate { Invalidate(); };
            Controls.Add(aspectBox);
            this.resolutionWidth = resolutionWidth;
            resolutionBox = new CheckBox {
                Text = L.Get("area"),
                Checked = matchArea, ForeColor = Color.White, BackColor = Color.FromArgb(24, 24, 24),
                Font = new Font("Segoe UI", 14f, GraphicsUnit.Pixel),
                AccessibleName = "Match desktop resolution to selected area"
            };
            resolutionList = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDownList,
                DropDownWidth = 440, IntegralHeight = false, MaxDropDownItems = 16,
                Font = new Font("Segoe UI", 14f, GraphicsUnit.Pixel),
                AccessibleName = "Desktop resolution"
            };
            resolutionBox.CheckedChanged += delegate { PositionAspectBox(); Invalidate(); };
            resolutionList.SelectedIndexChanged += delegate {
                if (updatingResolutions || resolutionList.SelectedItem == null) return;
                this.resolutionWidth = ((ResolutionChoice)resolutionList.SelectedItem).Width;
                Invalidate();
            };
            Controls.Add(resolutionBox);
            Controls.Add(resolutionList);
            languageLabel = new Label { Text = "Language / 语言", ForeColor = Color.White, BackColor = BackColor, TextAlign = ContentAlignment.MiddleRight };
            languageList = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Language" };
            languageList.Items.AddRange(L.Names);
            languageList.SelectedIndex = Array.IndexOf(L.Codes, L.Language);
            languageList.SelectedIndexChanged += delegate {
                if (languageList.SelectedIndex < 0) return;
                L.Language = L.Codes[languageList.SelectedIndex];
                try { L.SaveLanguage(); } catch (IOException ex) { MessageBox.Show(ex.Message, L.Caption); } catch (UnauthorizedAccessException ex) { MessageBox.Show(ex.Message, L.Caption); }
                aspectBox.Text = L.Get("aspect"); resolutionBox.Text = L.Get("area"); Text = L.Caption;
                PositionAspectBox(); Invalidate();
            };
            Controls.Add(languageLabel); Controls.Add(languageList);
            PositionAspectBox();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            ActiveControl = null;
            Activate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Render(e.Graphics, panel, CutLeft, CutTop, CutRight, CutBottom, Crisp, KeepAspect, MatchArea, ResolutionWidth);
            if (MatchArea && resolutionList.Items.Count == 0)
                using (var font = new Font("Segoe UI", 13f, GraphicsUnit.Pixel))
                    e.Graphics.DrawString(L.Get("noValidated"), font, Brushes.Orange,
                        new RectangleF(CutLeft + 20, panel.Height - CutBottom - 140, panel.Width - CutLeft - CutRight - 40, 40));
        }

        public static void Render(Graphics g, Size panel, int cutLeft, int cutTop, int cutRight, int cutBottom, bool crisp)
        {
            Render(g, panel, cutLeft, cutTop, cutRight, cutBottom, crisp, true);
        }

        public static void Render(Graphics g, Size panel, int cutLeft, int cutTop, int cutRight, int cutBottom, bool crisp, bool keepAspect)
        {
            Render(g, panel, cutLeft, cutTop, cutRight, cutBottom, crisp, keepAspect, false, 0);
        }

        public static void Render(Graphics g, Size panel, int cutLeft, int cutTop, int cutRight, int cutBottom, bool crisp, bool keepAspect, bool matchArea, int resolutionWidth)
        {
            int w = panel.Width, h = panel.Height, lineY = h - cutBottom, lineX = w - cutRight;
            Size desktop;
            Rectangle region;
            bool fits = Planner.Plan(panel, cutLeft, cutTop, cutRight, cutBottom, crisp, keepAspect, matchArea, resolutionWidth, out desktop, out region);

            g.Clear(Color.FromArgb(24, 24, 24));
            if (fits)
                using (var b = new SolidBrush(Color.FromArgb(30, 60, 42))) g.FillRectangle(b, region);
            using (var hatch = new HatchBrush(HatchStyle.WideDownwardDiagonal, Color.FromArgb(210, 50, 50), Color.FromArgb(70, 0, 0)))
            {
                g.FillRectangle(hatch, 0, 0, cutLeft, h);
                g.FillRectangle(hatch, lineX, 0, cutRight, h);
                g.FillRectangle(hatch, cutLeft, 0, lineX - cutLeft, cutTop);
                g.FillRectangle(hatch, cutLeft, lineY, lineX - cutLeft, cutBottom);
            }
            using (var yellow = new SolidBrush(Color.Yellow))
            {
                int thickness = Math.Min(8, Math.Min(lineX - cutLeft, lineY - cutTop));
                g.FillRectangle(yellow, cutLeft, cutTop, thickness, lineY - cutTop);
                g.FillRectangle(yellow, lineX - thickness, cutTop, thickness, lineY - cutTop);
                g.FillRectangle(yellow, cutLeft, cutTop, lineX - cutLeft, thickness);
                g.FillRectangle(yellow, cutLeft, lineY - thickness, lineX - cutLeft, thickness);
            }

            string mode = !fits ? L.Get("invalid") : matchArea ? L.F("desktop", desktop.Width, desktop.Height) :
                crisp ? L.F("crisp", desktop.Width, desktop.Height) : L.Get(keepAspect ? "fill" : "stretch");
            string body = L.F("instructions", cutLeft, cutTop, cutRight, cutBottom, mode);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var visible = new Rectangle(cutLeft + 8, cutTop + 48, lineX - cutLeft - 16, lineY - cutTop - 152);
            if (visible.Width <= 0 || visible.Height <= 0) return;
            using (var title = new Font("Segoe UI", Math.Max(12f, Math.Min(h / 30f, visible.Height / 10f)), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var font = new Font("Segoe UI", Math.Max(8f, Math.Min(h / 50f, Math.Min(visible.Height / 18f, visible.Width / 85f))), GraphicsUnit.Pixel))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center })
            {
                SizeF ts = g.MeasureString(L.Caption, title, visible.Width, fmt);
                SizeF bs = g.MeasureString(body, font, visible.Width, fmt);
                float top = visible.Top + (visible.Height - ts.Height - bs.Height) / 2;
                g.DrawString(L.Caption, title, Brushes.White, new RectangleF(visible.Left, top, visible.Width, ts.Height), fmt);
                g.DrawString(body, font, Brushes.Gainsboro, new RectangleF(visible.Left, top + ts.Height, visible.Width, bs.Height + 4), fmt);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            // Let the focused dropdown navigate resolutions without moving calibration edges.
            ComboBox focusedList = languageList.Focused ? languageList : resolutionList;
            if (focusedList.Focused && (code == Keys.Up || code == Keys.Down || code == Keys.Left || code == Keys.Right ||
                code == Keys.Home || code == Keys.End || code == Keys.PageUp || code == Keys.PageDown || code == Keys.F4 ||
                (focusedList.DroppedDown && (code == Keys.Enter || code == Keys.Escape))))
                return base.ProcessCmdKey(ref msg, keyData);
            int step = (keyData & Keys.Alt) != 0 ? 1 : (keyData & Keys.Shift) != 0 ? 20 : 4;
            int maxX = Math.Max(0, panel.Width - Math.Min(640, panel.Width));
            int maxY = Math.Max(0, panel.Height - Math.Min(360, panel.Height));
            bool opposite = (keyData & Keys.Control) != 0;
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: if (opposite) CutRight = Math.Min(maxX - CutLeft, CutRight + step); else CutLeft -= step; break;
                case Keys.Right: if (opposite) CutRight -= step; else CutLeft = Math.Min(maxX - CutRight, CutLeft + step); break;
                case Keys.Up: if (opposite) CutTop -= step; else CutBottom = Math.Min(maxY - CutTop, CutBottom + step); break;
                case Keys.Down: if (opposite) CutTop = Math.Min(maxY - CutBottom, CutTop + step); else CutBottom -= step; break;
                case Keys.Tab: if (!MatchArea) Crisp = !Crisp; break;
                case Keys.A: if (!Crisp && !MatchArea) aspectBox.Checked = !aspectBox.Checked; break;
                case Keys.D: resolutionBox.Checked = !resolutionBox.Checked; break;
                case Keys.F6: if (MatchArea) { resolutionList.Focus(); resolutionList.DroppedDown = true; } return true;
                case Keys.F7: languageList.Focus(); languageList.DroppedDown = true; return true;
                case Keys.Enter:
                    if (MatchArea && resolutionList.SelectedItem == null) { MessageBox.Show(L.Get("noValidated"), L.Caption); return true; }
                    DialogResult = DialogResult.OK; return true;
                case Keys.R: DialogResult = DialogResult.Retry; return true;
                case Keys.Escape: DialogResult = DialogResult.Cancel; return true;
                default: return base.ProcessCmdKey(ref msg, keyData);
            }
            ClampCuts();
            PositionAspectBox();
            Invalidate();
            return true;
        }

        void PositionAspectBox()
        {
            languageLabel.SetBounds(CutLeft + 16, CutTop + 12, 130, 26);
            languageList.SetBounds(CutLeft + 154, CutTop + 12, 160, 26);
            aspectBox.Enabled = !Crisp && !MatchArea;
            int width = Math.Min(480, Math.Max(1, panel.Width - CutLeft - CutRight - 32));
            aspectBox.SetBounds(CutLeft + (panel.Width - CutLeft - CutRight - width) / 2,
                panel.Height - CutBottom - 96, width, 32);
            int rowWidth = Math.Min(600, panel.Width - CutLeft - CutRight - 32);
            int left = CutLeft + (panel.Width - CutLeft - CutRight - rowWidth) / 2;
            int listWidth = Math.Min(240, rowWidth / 2);
            resolutionBox.SetBounds(left, panel.Height - CutBottom - 56, rowWidth - listWidth, 32);
            resolutionList.SetBounds(left + rowWidth - listWidth, panel.Height - CutBottom - 52, listWidth, 32);
            UpdateResolutions();
            resolutionList.Enabled = MatchArea && resolutionList.Items.Count > 0;
        }

        sealed class ResolutionChoice
        {
            public int Width;
            public string Label;
            public override string ToString() { return Label; }
        }

        void UpdateResolutions()
        {
            Size area = new Size(panel.Width - CutLeft - CutRight, panel.Height - CutTop - CutBottom);
            updatingResolutions = true;
            resolutionList.BeginUpdate();
            try
            {
                resolutionList.Items.Clear();
                Rectangle rectangle = new Rectangle(CutLeft, CutTop, area.Width, area.Height);
                if (validatedModes == null || validatedArea != rectangle)
                {
                    validatedModes = Planner.ValidatedNearestModes(rectangle, Planner.DriverModes(), validateMode);
                    validatedArea = rectangle;
                }
                int selected = 0;
                double best = double.MaxValue;
                Size desired = Planner.AreaResolution(area, resolutionWidth);
                foreach (Size s in validatedModes)
                {
                    int w = Planner.DriverKey(s);
                    var choice = new ResolutionChoice { Width = w, Label = s.Width + " × " + s.Height + " — " + Planner.AspectLabel(s) + L.Get("validated") };
                    int index = resolutionList.Items.Add(choice);
                    double distance = w == resolutionWidth ? -1 : Math.Abs(Math.Log((double)s.Width / Math.Max(1, desired.Width)));
                    if (distance < best) { best = distance; selected = index; }
                }
                if (resolutionList.Items.Count > 0)
                {
                    resolutionList.SelectedIndex = selected;
                    resolutionWidth = ((ResolutionChoice)resolutionList.SelectedItem).Width;
                }
                else resolutionWidth = 0;
            }
            finally { resolutionList.EndUpdate(); updatingResolutions = false; }
        }

        void ClampCuts()
        {
            // Keep enough room for calibration text, without the old one-third-per-edge limit.
            int maxX = Math.Max(0, panel.Width - Math.Min(640, panel.Width));
            int maxY = Math.Max(0, panel.Height - Math.Min(360, panel.Height));
            CutRight = Math.Max(0, Math.Min(maxX, CutRight));
            CutLeft = Math.Max(0, Math.Min(maxX - CutRight, CutLeft));
            CutRight = Math.Min(maxX - CutLeft, CutRight);
            CutBottom = Math.Max(0, Math.Min(maxY, CutBottom));
            CutTop = Math.Max(0, Math.Min(maxY - CutBottom, CutTop));
            CutBottom = Math.Min(maxY - CutTop, CutBottom);
        }
    }

    public class Confirm : Form
    {
        int secondsLeft = 20;
        readonly Label label;
        readonly Timer timer = new Timer();

        public Confirm()
        {
            float k;
            using (var g = CreateGraphics()) k = g.DpiX / 96f;
            Text = L.Caption;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 11f);
            ClientSize = new Size((int)(480 * k), (int)(150 * k));
            label = new Label { Left = (int)(16 * k), Top = (int)(12 * k), Width = (int)(448 * k), Height = (int)(76 * k) };
            var keep = new Button { Text = L.T("Оставить", "Keep"), Left = (int)(16 * k), Top = (int)(96 * k), Width = (int)(216 * k), Height = (int)(40 * k) };
            var back = new Button { Text = L.T("Вернуть как было", "Revert"), Left = (int)(248 * k), Top = (int)(96 * k), Width = (int)(216 * k), Height = (int)(40 * k) };
            keep.Click += delegate { DialogResult = DialogResult.Yes; };
            back.Click += delegate { DialogResult = DialogResult.No; };
            CancelButton = back;
            Controls.AddRange(new Control[] { label, keep, back });
            ActiveControl = back; // a held Enter from the calibration screen must not keep the change
            timer.Interval = 1000;
            timer.Tick += delegate
            {
                if (--secondsLeft <= 0) DialogResult = DialogResult.No;
                else UpdateText();
            };
            UpdateText();
            timer.Start();
        }

        void UpdateText()
        {
            label.Text = L.F("confirm", secondsLeft);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    public static class Program
    {
        static readonly string SettingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenShift", "settings.txt");
        static string Caption { get { return L.Caption; } }

        [STAThread]
        public static int Main(string[] args)
        {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { Native.SetProcessDPIAware(); }
            Application.EnableVisualStyles();
            L.LoadLanguage();

            string command = "";
            foreach (string arg in args)
            {
                string a = arg.ToLowerInvariant();
                if (a.StartsWith("--lang=")) L.Language = L.Normalize(a.Substring("--lang=".Length));
                else if (command.Length == 0) command = a;
            }
            try { return Run(command); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        static int Run(string command)
        {
            DisplayConfig current = DisplayConfig.Query();
            int cutLeft = 0, cutTop = 0, cutRight = 0, cutBottom = 0;
            bool crisp = false, keepAspect = true, matchArea = false;
            int resolutionWidth = 0;
            bool saved = Load(ref cutLeft, ref cutTop, ref cutRight, ref cutBottom, ref crisp, ref keepAspect, ref matchArea, ref resolutionWidth);

            if (command == "--reset")
            {
                Reset(current);
                return 0;
            }
            if (command == "--apply")
            {
                if (!saved) return 0;
                Size desktop;
                Rectangle region;
                if (!Planner.Plan(current.Panel, cutLeft, cutTop, cutRight, cutBottom, crisp, keepAspect, matchArea, resolutionWidth, out desktop, out region)) return 1;
                if (current.Desktop == desktop && current.Region == region) return 0;
                DisplayConfig candidate = current.With(desktop, region);
                DisplayConfig.Check(candidate.Apply(DisplayConfig.SDC_VALIDATE), L.Get("Could not apply the shift"));
                DisplayConfig.Check(candidate.Apply(DisplayConfig.SDC_APPLY | DisplayConfig.SDC_SAVE_TO_DATABASE),
                    L.T("Не удалось применить сдвиг", "Could not apply the shift"));
                return 0;
            }

            // Calibrate on the unshifted picture so overlay pixels are panel pixels.
            bool wasShifted = !current.IsIdentity;
            if (wasShifted)
                DisplayConfig.Check(current.Identity().Apply(DisplayConfig.SDC_APPLY),
                    L.T("Не удалось временно убрать сдвиг", "Could not temporarily remove the shift"));

            DialogResult result;
            using (var overlay = new Overlay(current.Panel, cutLeft, cutTop, cutRight, cutBottom, crisp, keepAspect, matchArea, resolutionWidth,
                (size, rectangle) => current.With(size, rectangle).Apply(DisplayConfig.SDC_VALIDATE)))
            {
                result = overlay.ShowDialog();
                cutLeft = overlay.CutLeft;
                cutTop = overlay.CutTop;
                cutRight = overlay.CutRight;
                cutBottom = overlay.CutBottom;
                crisp = overlay.Crisp;
                keepAspect = overlay.KeepAspect;
                matchArea = overlay.MatchArea;
                resolutionWidth = overlay.ResolutionWidth;
            }

            if (result == DialogResult.Retry)
            {
                Reset(current);
                MessageBox.Show(L.T("Сдвиг убран, картинка снова на весь экран.", "The shift is removed, the picture is full screen again."), Caption);
                return 0;
            }
            if (result != DialogResult.OK)
            {
                if (wasShifted) Restore(current);
                return 0;
            }

            Size newDesktop;
            Rectangle newRegion;
            if (!Planner.Plan(current.Panel, cutLeft, cutTop, cutRight, cutBottom, crisp, keepAspect, matchArea, resolutionWidth, out newDesktop, out newRegion))
            {
                if (wasShifted) Restore(current);
                MessageBox.Show(L.Get("invalid"), Caption);
                return 1;
            }
            DisplayConfig next = current.With(newDesktop, newRegion);
            int rc = next.Apply(DisplayConfig.SDC_VALIDATE);
            string stage = "SDC_VALIDATE";
            if (rc != 0 && matchArea && rc != 5 && rc != 50)
            {
                Rectangle area = new Rectangle(cutLeft, cutTop, current.Panel.Width - cutLeft - cutRight, current.Panel.Height - cutTop - cutBottom);
                var verified = Planner.ValidatedNearestModes(area, Planner.DriverModes(),
                    (size, rectangle) => current.With(size, rectangle).Apply(DisplayConfig.SDC_VALIDATE));
                foreach (Size candidate in Planner.CompatibleCandidates(newDesktop, area, verified))
                {
                    Rectangle candidateRegion = Planner.Fit(candidate, area);
                    if (candidate == newDesktop && candidateRegion == newRegion) continue;
                    DisplayConfig alternative = current.With(candidate, candidateRegion);
                    if (alternative.Apply(DisplayConfig.SDC_VALIDATE) != 0) continue;
                    // No changes have been applied. Ask before substituting the user's resolution.
                    if (MessageBox.Show(L.F("fallback", newDesktop.Width, newDesktop.Height, rc, candidate.Width, candidate.Height),
                        Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        if (wasShifted) Restore(current);
                        return 0;
                    }
                    newDesktop = candidate; newRegion = candidateRegion;
                    resolutionWidth = Planner.DriverKey(candidate);
                    next = alternative; rc = 0;
                    break;
                }
            }
            if (rc == 0) { stage = "SDC_APPLY"; rc = next.Apply(DisplayConfig.SDC_APPLY); }
            if (rc != 0)
            {
                if (wasShifted || stage == "SDC_APPLY") Restore(current);
                string detail = new System.ComponentModel.Win32Exception(rc).Message;
                string diagnostic = Path.Combine(Path.GetDirectoryName(SettingsFile), "last-error.txt");
                try {
                    Directory.CreateDirectory(Path.GetDirectoryName(diagnostic));
                    File.WriteAllText(diagnostic, "UTC: " + DateTime.UtcNow.ToString("o") + "\r\nStage: " + stage + "\r\nCode: " + rc +
                        "\r\nMessage: " + detail + "\r\nPanel: " + current.Panel + "\r\nDesktop: " + newDesktop + "\r\nRegion: " + newRegion +
                        "\r\nDriver modes: " + string.Join(", ", Planner.DriverModes().ConvertAll(s => s.Width + "x" + s.Height).ToArray()));
                } catch (IOException) { diagnostic = "(log unavailable)"; } catch (UnauthorizedAccessException) { diagnostic = "(log unavailable)"; }
                MessageBox.Show(L.F("rejected", stage, rc, newDesktop.Width, newDesktop.Height, newRegion, detail, diagnostic),
                    Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }

            DialogResult answer;
            using (var confirm = new Confirm()) answer = confirm.ShowDialog();
            if (answer == DialogResult.Yes)
            {
                DisplayConfig.Check(next.Apply(DisplayConfig.SDC_APPLY | DisplayConfig.SDC_SAVE_TO_DATABASE),
                    L.T("Не удалось сохранить сдвиг", "Could not save the shift"));
                Save(cutLeft, cutTop, cutRight, cutBottom, crisp, keepAspect, matchArea, resolutionWidth);
            }
            else
            {
                Restore(current);
            }
            return 0;
        }

        static void Restore(DisplayConfig original)
        {
            if (original.Apply(DisplayConfig.SDC_APPLY) != 0) DisplayConfig.ApplyDatabase();
        }

        static void Reset(DisplayConfig current)
        {
            DisplayConfig.Check(current.Identity().Apply(DisplayConfig.SDC_APPLY | DisplayConfig.SDC_SAVE_TO_DATABASE),
                L.T("Не удалось убрать сдвиг", "Could not remove the shift"));
            if (File.Exists(SettingsFile)) File.Delete(SettingsFile);
        }

        static bool Load(ref int cutLeft, ref int cutTop, ref int cutRight, ref int cutBottom, ref bool crisp, ref bool keepAspect, ref bool matchArea, ref int resolutionWidth)
        {
            if (!File.Exists(SettingsFile)) return false;
            string[] parts = File.ReadAllText(SettingsFile).Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int l, t = 0, r = 0, b;
            string mode;
            if (parts.Length == 3)
            {
                // Original settings: left bottom mode.
                if (!int.TryParse(parts[0], out l) || !int.TryParse(parts[1], out b)) return false;
                mode = parts[2];
            }
            else if (parts.Length == 5 || parts.Length == 6 || parts.Length == 8)
            {
                if (!int.TryParse(parts[0], out l) || !int.TryParse(parts[1], out t) ||
                    !int.TryParse(parts[2], out r) || !int.TryParse(parts[3], out b)) return false;
                mode = parts[4];
            }
            else return false;
            if (l < 0 || t < 0 || r < 0 || b < 0 || (mode != "crisp" && mode != "fill")) return false;
            if (parts.Length >= 6 && parts[5] != "aspect" && parts[5] != "stretch") return false;
            int width = 0;
            if (parts.Length == 8 && ((parts[6] != "area" && parts[6] != "original") ||
                !int.TryParse(parts[7], out width) || !Planner.ValidResolutionKey(width))) return false;
            matchArea = parts.Length == 8 && parts[6] == "area";
            resolutionWidth = width;
            keepAspect = parts.Length < 6 || parts[5] == "aspect";
            cutLeft = l;
            cutTop = t;
            cutRight = r;
            cutBottom = b;
            crisp = mode == "crisp";
            return true;
        }

        static void Save(int cutLeft, int cutTop, int cutRight, int cutBottom, bool crisp, bool keepAspect, bool matchArea, int resolutionWidth)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
            File.WriteAllText(SettingsFile, cutLeft + " " + cutTop + " " + cutRight + " " + cutBottom + " " + (crisp ? "crisp" : "fill") + " " + (keepAspect ? "aspect" : "stretch") + " " + (matchArea ? "area" : "original") + " " + resolutionWidth);
        }
    }
}
