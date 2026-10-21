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
    // UI language: Russian when Windows is set to Russian, English otherwise. `--lang=en|ru` overrides.
    public static class L
    {
        public static bool Russian = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
        public static string T(string ru, string en) { return Russian ? ru : en; }
        public static string Caption { get { return T("Сдвиг экрана", "Screen Shift"); } }
    }

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
                throw new Exception(L.T(
                    "Нужен ровно один включённый экран без внешних мониторов (сейчас путей: " + np + ").",
                    "Exactly one active display is required, with no external monitors (active paths: " + np + ")."));
            return c;
        }

        int Get(int offset) { return BitConverter.ToInt32(modes, offset); }
        void Set(int offset, int value) { BitConverter.GetBytes(value).CopyTo(modes, offset); }

        public Size Panel { get { return new Size(Get(tgt * ModeSize + 40), Get(tgt * ModeSize + 44)); } }
        public Size Desktop { get { return new Size(Get(src * ModeSize + 16), Get(src * ModeSize + 20)); } }
        public Size PathSource { get { return new Size(Get(img * ModeSize + 16), Get(img * ModeSize + 20)); } }
        public Rectangle Region
        {
            get { int o = img * ModeSize + 24; return Rectangle.FromLTRB(Get(o), Get(o + 4), Get(o + 8), Get(o + 12)); }
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
            Size panel = Panel;
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
            desktop = Size.Empty;
            region = Rectangle.Empty;
            if (panel.Width <= 0 || panel.Height <= 0 || cutLeft < 0 || cutTop < 0 || cutRight < 0 || cutBottom < 0 ||
                (long)cutLeft + cutRight >= panel.Width || (long)cutTop + cutBottom >= panel.Height) return false;
            int vw = panel.Width - cutLeft - cutRight, vh = panel.Height - cutTop - cutBottom;
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
        readonly Size panel;

        public Overlay(Size panel, int cutLeft, int cutTop, int cutRight, int cutBottom, bool crisp)
        {
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
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Activate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Render(e.Graphics, panel, CutLeft, CutTop, CutRight, CutBottom, Crisp);
        }

        public static void Render(Graphics g, Size panel, int cutLeft, int cutTop, int cutRight, int cutBottom, bool crisp)
        {
            int w = panel.Width, h = panel.Height, lineY = h - cutBottom, lineX = w - cutRight;
            Size desktop;
            Rectangle region;
            bool fits = Planner.Plan(panel, cutLeft, cutTop, cutRight, cutBottom, crisp, out desktop, out region);

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

            string mode = crisp
                ? (fits
                    ? string.Format(L.T("чётко, без сжатия (рабочий стол {0}×{1})", "crisp, 1:1 (desktop {0}×{1})"), desktop.Width, desktop.Height)
                    : L.T("чётко — не помещается, уменьшите полосы", "crisp — does not fit, reduce the strips"))
                : string.Format(L.T("вся картинка, сжата до {0:0.#}%", "fill, scaled to {0:0.#}%"), 100.0 * region.Width / w);
            string body = L.Russian
                ? "Жёлтые линии — новые края картинки. Всё красное станет чёрным.\n" +
                  "← →: левая; ↑ ↓: нижняя; Ctrl + ← →: правая; Ctrl + ↑ ↓: верхняя\n" +
                  "Шаг: 4 px; Shift: 20 px; Alt: 1 px\n" +
                  "Подведите каждую линию вплотную к чёрной полосе, но так, чтобы её ещё было видно.\n" +
                  "Если клавиши не действуют — щёлкните по экрану.\n\n" +
                  string.Format("Слева: {0} px   Сверху: {1} px   Справа: {2} px   Снизу: {3} px\n", cutLeft, cutTop, cutRight, cutBottom) +
                  "Tab — режим: " + mode + "\n\n" +
                  "Enter — применить      R — убрать сдвиг      Esc — выйти"
                : "Yellow lines are the new edges of the picture. Everything red will turn black.\n" +
                  "← →: left edge; ↑ ↓: bottom edge; Ctrl + ← →: right edge; Ctrl + ↑ ↓: top edge\n" +
                  "Step: 4 px; Shift: 20 px; Alt: 1 px\n" +
                  "Place each line right next to the dead strip, but keep the strip itself out of view.\n" +
                  "If the keys do nothing, click on the screen.\n\n" +
                  string.Format("Left: {0} px   Top: {1} px   Right: {2} px   Bottom: {3} px\n", cutLeft, cutTop, cutRight, cutBottom) +
                  "Tab — mode: " + mode + "\n\n" +
                  "Enter — apply      R — remove the shift      Esc — exit";

            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var visible = new Rectangle(cutLeft + 8, cutTop + 8, lineX - cutLeft - 16, lineY - cutTop - 16);
            if (visible.Width <= 0 || visible.Height <= 0) return;
            using (var title = new Font("Segoe UI", h / 30f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var font = new Font("Segoe UI", h / 50f, GraphicsUnit.Pixel))
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
                case Keys.Tab: Crisp = !Crisp; break;
                case Keys.Enter: DialogResult = DialogResult.OK; return true;
                case Keys.R: DialogResult = DialogResult.Retry; return true;
                case Keys.Escape: DialogResult = DialogResult.Cancel; return true;
                default: return base.ProcessCmdKey(ref msg, keyData);
            }
            ClampCuts();
            Invalidate();
            return true;
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
            label.Text = L.T(
                "Картинку видно целиком?\nЕсли ничего не нажать, через " + secondsLeft + " с всё вернётся как было.",
                "Can you see the whole picture?\nIf you press nothing, everything reverts in " + secondsLeft + " s.");
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

            string command = "";
            foreach (string arg in args)
            {
                string a = arg.ToLowerInvariant();
                if (a.StartsWith("--lang=")) L.Russian = a.Substring("--lang=".Length) == "ru";
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
            bool crisp = false;
            bool saved = Load(ref cutLeft, ref cutTop, ref cutRight, ref cutBottom, ref crisp);

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
                if (!Planner.Plan(current.Panel, cutLeft, cutTop, cutRight, cutBottom, crisp, out desktop, out region)) return 1;
                if (current.Desktop == desktop && current.Region == region) return 0;
                DisplayConfig.Check(current.With(desktop, region).Apply(DisplayConfig.SDC_APPLY | DisplayConfig.SDC_SAVE_TO_DATABASE),
                    L.T("Не удалось применить сдвиг", "Could not apply the shift"));
                return 0;
            }

            // Calibrate on the unshifted picture so overlay pixels are panel pixels.
            bool wasShifted = !current.IsIdentity;
            if (wasShifted)
                DisplayConfig.Check(current.Identity().Apply(DisplayConfig.SDC_APPLY),
                    L.T("Не удалось временно убрать сдвиг", "Could not temporarily remove the shift"));

            DialogResult result;
            using (var overlay = new Overlay(current.Panel, cutLeft, cutTop, cutRight, cutBottom, crisp))
            {
                result = overlay.ShowDialog();
                cutLeft = overlay.CutLeft;
                cutTop = overlay.CutTop;
                cutRight = overlay.CutRight;
                cutBottom = overlay.CutBottom;
                crisp = overlay.Crisp;
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
            if (!Planner.Plan(current.Panel, cutLeft, cutTop, cutRight, cutBottom, crisp, out newDesktop, out newRegion))
            {
                if (wasShifted) Restore(current);
                MessageBox.Show(L.T(
                    "В режиме «чётко» картинка не помещается. Выберите Tab режим «вся картинка».",
                    "The picture does not fit in crisp mode. Press Tab to switch to fill mode."), Caption);
                return 1;
            }
            DisplayConfig next = current.With(newDesktop, newRegion);
            int rc = next.Apply(DisplayConfig.SDC_VALIDATE);
            if (rc == 0) rc = next.Apply(DisplayConfig.SDC_APPLY);
            if (rc != 0)
            {
                if (wasShifted) Restore(current);
                MessageBox.Show(L.T(
                    "Windows не приняла такой режим (код " + rc + "). Ничего не изменено.",
                    "Windows rejected this mode (code " + rc + "). Nothing was changed."), Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }

            DialogResult answer;
            using (var confirm = new Confirm()) answer = confirm.ShowDialog();
            if (answer == DialogResult.Yes)
            {
                DisplayConfig.Check(next.Apply(DisplayConfig.SDC_APPLY | DisplayConfig.SDC_SAVE_TO_DATABASE),
                    L.T("Не удалось сохранить сдвиг", "Could not save the shift"));
                Save(cutLeft, cutTop, cutRight, cutBottom, crisp);
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

        static bool Load(ref int cutLeft, ref int cutTop, ref int cutRight, ref int cutBottom, ref bool crisp)
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
            else if (parts.Length == 5)
            {
                if (!int.TryParse(parts[0], out l) || !int.TryParse(parts[1], out t) ||
                    !int.TryParse(parts[2], out r) || !int.TryParse(parts[3], out b)) return false;
                mode = parts[4];
            }
            else return false;
            if (l < 0 || t < 0 || r < 0 || b < 0 || (mode != "crisp" && mode != "fill")) return false;
            cutLeft = l;
            cutTop = t;
            cutRight = r;
            cutBottom = b;
            crisp = mode == "crisp";
            return true;
        }

        static void Save(int cutLeft, int cutTop, int cutRight, int cutBottom, bool crisp)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
            File.WriteAllText(SettingsFile, cutLeft + " " + cutTop + " " + cutRight + " " + cutBottom + " " + (crisp ? "crisp" : "fill"));
        }
    }
}
