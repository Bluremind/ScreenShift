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
                throw new Exception("Нужен ровно один включённый экран без внешних мониторов (сейчас путей: " + np + ").");
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
            if (rc != 0) throw new Exception(what + ": код ошибки " + rc + ".");
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

        // Visible part of the panel is [cutLeft, W) x [0, H - cutBottom).
        public static bool Plan(Size panel, int cutLeft, int cutBottom, bool crisp, out Size desktop, out Rectangle region)
        {
            int vw = panel.Width - cutLeft, vh = panel.Height - cutBottom;
            if (crisp)
            {
                foreach (Size m in CrispModes(panel))
                {
                    if (m.Width > vw || m.Height > vh) continue;
                    desktop = m;
                    region = new Rectangle(cutLeft + (vw - m.Width) / 2, (vh - m.Height) / 2, m.Width, m.Height);
                    return true;
                }
                desktop = Size.Empty;
                region = Rectangle.Empty;
                return false;
            }
            double s = Math.Min((double)vw / panel.Width, (double)vh / panel.Height);
            int rw = (int)Math.Round(panel.Width * s), rh = (int)Math.Round(panel.Height * s);
            desktop = panel;
            region = new Rectangle(cutLeft + (vw - rw) / 2, (vh - rh) / 2, rw, rh);
            return true;
        }
    }

    public class Overlay : Form
    {
        public int CutLeft, CutBottom;
        public bool Crisp;
        readonly Size panel;

        public Overlay(Size panel, int cutLeft, int cutBottom, bool crisp)
        {
            this.panel = panel;
            CutLeft = cutLeft;
            CutBottom = cutBottom;
            Crisp = crisp;
            Text = "Сдвиг экрана";
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
            Render(e.Graphics, panel, CutLeft, CutBottom, Crisp);
        }

        public static void Render(Graphics g, Size panel, int cutLeft, int cutBottom, bool crisp)
        {
            int w = panel.Width, h = panel.Height, lineY = h - cutBottom;
            Size desktop;
            Rectangle region;
            bool fits = Planner.Plan(panel, cutLeft, cutBottom, crisp, out desktop, out region);

            g.Clear(Color.FromArgb(24, 24, 24));
            if (fits)
                using (var b = new SolidBrush(Color.FromArgb(30, 60, 42))) g.FillRectangle(b, region);
            using (var hatch = new HatchBrush(HatchStyle.WideDownwardDiagonal, Color.FromArgb(210, 50, 50), Color.FromArgb(70, 0, 0)))
            {
                g.FillRectangle(hatch, 0, 0, cutLeft, h);
                g.FillRectangle(hatch, cutLeft, lineY, w - cutLeft, cutBottom);
            }
            using (var yellow = new SolidBrush(Color.Yellow))
            {
                g.FillRectangle(yellow, cutLeft, 0, 8, lineY);
                g.FillRectangle(yellow, cutLeft, lineY - 8, w - cutLeft, 8);
            }

            string mode = crisp
                ? (fits ? string.Format("чётко, без сжатия (рабочий стол {0}×{1})", desktop.Width, desktop.Height) : "чётко — не помещается, уменьшите полосы")
                : string.Format("вся картинка, сжата до {0:0.#}%", 100.0 * region.Width / w);
            string body =
                "Жёлтые линии — новые края картинки. Всё красное станет чёрным.\n" +
                "← →  двигать левую линию      ↑ ↓  двигать нижнюю      Shift — быстрее\n" +
                "Подведите каждую линию вплотную к чёрной полосе, но так, чтобы её ещё было видно.\n" +
                "Если клавиши не действуют — щёлкните по экрану.\n\n" +
                string.Format("Слева убрать: {0} px      Снизу убрать: {1} px\n", cutLeft, cutBottom) +
                "Tab — режим: " + mode + "\n\n" +
                "Enter — применить      R — убрать сдвиг      Esc — выйти";

            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var visible = new Rectangle(cutLeft + 8, 0, w - cutLeft - 8, lineY - 8);
            using (var title = new Font("Segoe UI", h / 30f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var font = new Font("Segoe UI", h / 50f, GraphicsUnit.Pixel))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center })
            {
                SizeF ts = g.MeasureString("Сдвиг экрана", title, visible.Width, fmt);
                SizeF bs = g.MeasureString(body, font, visible.Width, fmt);
                float top = visible.Top + (visible.Height - ts.Height - bs.Height) / 2;
                g.DrawString("Сдвиг экрана", title, Brushes.White, new RectangleF(visible.Left, top, visible.Width, ts.Height), fmt);
                g.DrawString(body, font, Brushes.Gainsboro, new RectangleF(visible.Left, top + ts.Height, visible.Width, bs.Height + 4), fmt);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            int step = (keyData & Keys.Shift) != 0 ? 20 : 4;
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: CutLeft -= step; break;
                case Keys.Right: CutLeft += step; break;
                case Keys.Up: CutBottom += step; break;
                case Keys.Down: CutBottom -= step; break;
                case Keys.Tab: Crisp = !Crisp; break;
                case Keys.Enter: DialogResult = DialogResult.OK; return true;
                case Keys.R: DialogResult = DialogResult.Retry; return true;
                case Keys.Escape: DialogResult = DialogResult.Cancel; return true;
                default: return base.ProcessCmdKey(ref msg, keyData);
            }
            CutLeft = Math.Max(0, Math.Min(panel.Width / 3, CutLeft));
            CutBottom = Math.Max(0, Math.Min(panel.Height / 3, CutBottom));
            Invalidate();
            return true;
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
            Text = "Сдвиг экрана";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 11f);
            ClientSize = new Size((int)(480 * k), (int)(150 * k));
            label = new Label { Left = (int)(16 * k), Top = (int)(12 * k), Width = (int)(448 * k), Height = (int)(76 * k) };
            var keep = new Button { Text = "Оставить", Left = (int)(16 * k), Top = (int)(96 * k), Width = (int)(216 * k), Height = (int)(40 * k) };
            var back = new Button { Text = "Вернуть как было", Left = (int)(248 * k), Top = (int)(96 * k), Width = (int)(216 * k), Height = (int)(40 * k) };
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
            label.Text = "Картинку видно целиком?\nЕсли ничего не нажать, через " + secondsLeft + " с всё вернётся как было.";
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
        const string Caption = "Сдвиг экрана";

        [STAThread]
        public static int Main(string[] args)
        {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { Native.SetProcessDPIAware(); }
            Application.EnableVisualStyles();
            try { return Run(args.Length > 0 ? args[0].ToLowerInvariant() : ""); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        static int Run(string command)
        {
            DisplayConfig current = DisplayConfig.Query();
            int cutLeft = 200, cutBottom = 200;
            bool crisp = false;
            bool saved = Load(ref cutLeft, ref cutBottom, ref crisp);

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
                if (!Planner.Plan(current.Panel, cutLeft, cutBottom, crisp, out desktop, out region)) return 1;
                if (current.Desktop == desktop && current.Region == region) return 0;
                DisplayConfig.Check(current.With(desktop, region).Apply(DisplayConfig.SDC_APPLY | DisplayConfig.SDC_SAVE_TO_DATABASE), "Не удалось применить сдвиг");
                return 0;
            }

            // Calibrate on the unshifted picture so overlay pixels are panel pixels.
            bool wasShifted = !current.IsIdentity;
            if (wasShifted)
                DisplayConfig.Check(current.Identity().Apply(DisplayConfig.SDC_APPLY), "Не удалось временно убрать сдвиг");

            DialogResult result;
            using (var overlay = new Overlay(current.Panel, cutLeft, cutBottom, crisp))
            {
                result = overlay.ShowDialog();
                cutLeft = overlay.CutLeft;
                cutBottom = overlay.CutBottom;
                crisp = overlay.Crisp;
            }

            if (result == DialogResult.Retry)
            {
                Reset(current);
                MessageBox.Show("Сдвиг убран, картинка снова на весь экран.", Caption);
                return 0;
            }
            if (result != DialogResult.OK)
            {
                if (wasShifted) Restore(current);
                return 0;
            }

            Size newDesktop;
            Rectangle newRegion;
            if (!Planner.Plan(current.Panel, cutLeft, cutBottom, crisp, out newDesktop, out newRegion))
            {
                if (wasShifted) Restore(current);
                MessageBox.Show("В режиме «чётко» картинка не помещается. Выберите Tab режим «вся картинка».", Caption);
                return 1;
            }
            DisplayConfig next = current.With(newDesktop, newRegion);
            int rc = next.Apply(DisplayConfig.SDC_VALIDATE);
            if (rc == 0) rc = next.Apply(DisplayConfig.SDC_APPLY);
            if (rc != 0)
            {
                if (wasShifted) Restore(current);
                MessageBox.Show("Windows не приняла такой режим (код " + rc + "). Ничего не изменено.", Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }

            DialogResult answer;
            using (var confirm = new Confirm()) answer = confirm.ShowDialog();
            if (answer == DialogResult.Yes)
            {
                DisplayConfig.Check(next.Apply(DisplayConfig.SDC_APPLY | DisplayConfig.SDC_SAVE_TO_DATABASE), "Не удалось сохранить сдвиг");
                Save(cutLeft, cutBottom, crisp);
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
            DisplayConfig.Check(current.Identity().Apply(DisplayConfig.SDC_APPLY | DisplayConfig.SDC_SAVE_TO_DATABASE), "Не удалось убрать сдвиг");
            if (File.Exists(SettingsFile)) File.Delete(SettingsFile);
        }

        static bool Load(ref int cutLeft, ref int cutBottom, ref bool crisp)
        {
            if (!File.Exists(SettingsFile)) return false;
            string[] parts = File.ReadAllText(SettingsFile).Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int l, b;
            if (parts.Length < 3 || !int.TryParse(parts[0], out l) || !int.TryParse(parts[1], out b)) return false;
            cutLeft = l;
            cutBottom = b;
            crisp = parts[2] == "crisp";
            return true;
        }

        static void Save(int cutLeft, int cutBottom, bool crisp)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
            File.WriteAllText(SettingsFile, cutLeft + " " + cutBottom + " " + (crisp ? "crisp" : "fill"));
        }
    }
}
