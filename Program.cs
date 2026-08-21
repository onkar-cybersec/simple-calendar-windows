using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SimpleCalendar
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            bool preview = args.Length > 0 && args[0] == "--preview";
            if (args.Length == 2 && args[0] == "--make-icon")
            {
                using (Icon icon = CalendarIcon.Create(DateTime.Today, Theme.Accent, 64))
                using (FileStream stream = File.Create(args[1]))
                    icon.Save(stream);
                return;
            }

            if (args.Length == 2 && args[0] == "--make-assets")
            {
                TileAssets.CreateAll(args[1]);
                return;
            }

            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Preview builds can skip package-only live-tile scheduling; normal
            // installed launches keep the widget update path exactly as before.
            if (!preview) TileService.UpdateAndSchedule();
            Application.Run(new CalendarForm());
        }
    }

    internal static class Theme
    {
        public static bool IsDark
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                        return Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0;
                }
                catch { return false; }
            }
        }

        public static Color Accent
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                    {
                        uint value = Convert.ToUInt32(key.GetValue("ColorizationColor", 0xFF0078D4u));
                        Color color = Color.FromArgb((int)((value >> 16) & 255), (int)((value >> 8) & 255), (int)(value & 255));
                        if (color.GetBrightness() < .22f) return Color.FromArgb(0, 120, 212);
                        return color;
                    }
                }
                catch { return Color.FromArgb(0, 120, 212); }
            }
        }
    }

    internal static class WindowBackdrop
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaSystemBackdropType = 38;
        private const int DwmSystemBackdropNone = 1;
        private const int WindowCompositionAttributeAccentPolicy = 19;
        private const int AccentDisabled = 0;
        private const int AccentEnableBlurBehind = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left, Right, Top, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int State;
            public int Flags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int valueSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr handle, ref Margins margins);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr handle, ref WindowCompositionAttributeData data);

        public static bool Apply(IntPtr handle, bool enabled, bool dark)
        {
            try
            {
                int darkMode = dark ? 1 : 0;
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

                Margins margins = enabled
                    ? new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 }
                    : new Margins();
                DwmExtendFrameIntoClientArea(handle, ref margins);

                // The system backdrop API can return success while providing a
                // wallpaper-derived surface. Accent acrylic is what reveals and
                // blurs the actual windows directly behind this calendar.
                int backdrop = DwmSystemBackdropNone;
                DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
                return ApplyLegacy(handle, enabled, dark);
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        private static bool ApplyLegacy(IntPtr handle, bool enabled, bool dark)
        {
            AccentPolicy policy = new AccentPolicy();
            policy.State = enabled ? AccentEnableBlurBehind : AccentDisabled;
            policy.Flags = 0;
            policy.GradientColor = 0;

            int size = Marshal.SizeOf(typeof(AccentPolicy));
            IntPtr pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, pointer, false);
                WindowCompositionAttributeData data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttributeAccentPolicy,
                    Data = pointer,
                    SizeOfData = size
                };
                return SetWindowCompositionAttribute(handle, ref data) != 0;
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }

    }

    internal enum HolidayRegion
    {
        India,
        UnitedStates
    }

    internal sealed class HolidayInfo
    {
        public readonly string Name;
        public readonly string ShortName;
        public readonly HolidayRegion Region;

        public HolidayInfo(string name, string shortName, HolidayRegion region)
        {
            Name = name;
            ShortName = shortName;
            Region = region;
        }
    }

    internal static class HolidayCalendar
    {
        private static readonly Dictionary<int, Dictionary<DateTime, List<HolidayInfo>>> Years =
            new Dictionary<int, Dictionary<DateTime, List<HolidayInfo>>>();
        private static readonly IList<HolidayInfo> Empty = new HolidayInfo[0];

        public static IList<HolidayInfo> Get(DateTime date)
        {
            Dictionary<DateTime, List<HolidayInfo>> year;
            if (!Years.TryGetValue(date.Year, out year))
            {
                year = BuildYear(date.Year);
                Years[date.Year] = year;
            }

            List<HolidayInfo> holidays;
            return year.TryGetValue(date.Date, out holidays) ? holidays : Empty;
        }

        private static Dictionary<DateTime, List<HolidayInfo>> BuildYear(int year)
        {
            Dictionary<DateTime, List<HolidayInfo>> result = new Dictionary<DateTime, List<HolidayInfo>>();

            Add(result, year, new DateTime(year, 1, 26), "Republic Day", "Republic Day", HolidayRegion.India);
            Add(result, year, new DateTime(year, 8, 15), "Independence Day", "Independence Day", HolidayRegion.India);
            Add(result, year, new DateTime(year, 10, 2), "Mahatma Gandhi's Birthday", "Gandhi Jayanti", HolidayRegion.India);

            // 2026 gazetted and restricted dates published for Central Government
            // offices in India. Regional/lunar observance can vary by location.
            // Moon-sighting holidays can be moved by an official local announcement.
            if (year == 2026)
            {
                Add(result, year, new DateTime(2026, 1, 14), "Makar Sankranti", "Makar Sankranti", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 1, 15), "Pongal / Makar Sankranti", "Pongal / Sankranti", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 1, 23), "Basant Panchami / Sri Panchami", "Basant Panchami", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 2, 15), "Maha Shivaratri", "Maha Shivaratri", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 3, 3), "Holika Dahan / Dolyatra", "Holika Dahan", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 3, 4), "Holi", "Holi", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 3, 19), "Ugadi / Gudi Padwa / Chaitra Sukladi", "Ugadi / Gudi Padwa", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 3, 21), "Id-ul-Fitr (Ramzan)", "Eid al-Fitr", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 3, 26), "Ram Navami", "Ram Navami", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 3, 31), "Mahavir Jayanti", "Mahavir Jayanti", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 4, 3), "Good Friday", "Good Friday", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 4, 14), "Vaisakhi / Vishu / Tamil New Year", "Vishu / Tamil New Year", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 5, 1), "Buddha Purnima", "Buddha Purnima", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 5, 28), "Id-ul-Zuha (Bakrid)", "Eid al-Adha", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 6, 26), "Muharram", "Muharram", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 7, 16), "Rath Yatra", "Rath Yatra", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 8, 26), "Prophet Mohammad's Birthday (Id-e-Milad)", "Id-e-Milad", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 8, 26), "Onam / Thiru Onam", "Onam", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 8, 28), "Raksha Bandhan / Varamahalakshmi Vrata", "Raksha Bandhan", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 9, 4), "Janmashtami / Sree Krishna Jayanti", "Janmashtami", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 9, 14), "Ganesh Chaturthi / Vinayak Chaturthi", "Ganesh Chaturthi", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 10, 19), "Dussehra (Vijaya Dasami)", "Dussehra", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 10, 20), "Additional Dussehra holiday", "Dussehra holiday", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 11, 8), "Diwali (Deepavali)", "Diwali", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 11, 9), "Govardhan Puja", "Govardhan Puja", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 11, 11), "Bhai Duj", "Bhai Duj", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 11, 15), "Chhath Puja / Surya Shashthi", "Chhath Puja", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 11, 24), "Guru Nanak's Birthday", "Guru Nanak Jayanti", HolidayRegion.India);
                Add(result, year, new DateTime(2026, 12, 25), "Christmas Day", "Christmas", HolidayRegion.India);
            }

            AddUnitedStatesYear(result, year, year - 1);
            AddUnitedStatesYear(result, year, year);
            AddUnitedStatesYear(result, year, year + 1);
            return result;
        }

        private static void AddUnitedStatesYear(Dictionary<DateTime, List<HolidayInfo>> result, int targetYear, int holidayYear)
        {
            AddUsFixed(result, targetYear, new DateTime(holidayYear, 1, 1), "New Year's Day", "New Year's Day");
            Add(result, targetYear, NthWeekday(holidayYear, 1, DayOfWeek.Monday, 3), "Birthday of Martin Luther King, Jr.", "MLK Day", HolidayRegion.UnitedStates);
            Add(result, targetYear, NthWeekday(holidayYear, 2, DayOfWeek.Monday, 3), "Washington's Birthday", "Washington's Birthday", HolidayRegion.UnitedStates);
            Add(result, targetYear, LastWeekday(holidayYear, 5, DayOfWeek.Monday), "Memorial Day", "Memorial Day", HolidayRegion.UnitedStates);
            if (holidayYear >= 2021)
                AddUsFixed(result, targetYear, new DateTime(holidayYear, 6, 19), "Juneteenth National Independence Day", "Juneteenth");
            AddUsFixed(result, targetYear, new DateTime(holidayYear, 7, 4), "Independence Day", "Independence Day");
            Add(result, targetYear, NthWeekday(holidayYear, 9, DayOfWeek.Monday, 1), "Labor Day", "Labor Day", HolidayRegion.UnitedStates);
            Add(result, targetYear, NthWeekday(holidayYear, 10, DayOfWeek.Monday, 2), "Columbus Day", "Columbus Day", HolidayRegion.UnitedStates);
            AddUsFixed(result, targetYear, new DateTime(holidayYear, 11, 11), "Veterans Day", "Veterans Day");
            Add(result, targetYear, NthWeekday(holidayYear, 11, DayOfWeek.Thursday, 4), "Thanksgiving Day", "Thanksgiving", HolidayRegion.UnitedStates);
            AddUsFixed(result, targetYear, new DateTime(holidayYear, 12, 25), "Christmas Day", "Christmas");
        }

        private static void AddUsFixed(Dictionary<DateTime, List<HolidayInfo>> result, int targetYear, DateTime date, string name, string shortName)
        {
            Add(result, targetYear, date, name, shortName, HolidayRegion.UnitedStates);
            DateTime observed = date.DayOfWeek == DayOfWeek.Saturday ? date.AddDays(-1)
                : date.DayOfWeek == DayOfWeek.Sunday ? date.AddDays(1) : date;
            if (observed != date)
                Add(result, targetYear, observed, name + " (observed)", shortName + " (observed)", HolidayRegion.UnitedStates);
        }

        private static DateTime NthWeekday(int year, int month, DayOfWeek day, int occurrence)
        {
            DateTime first = new DateTime(year, month, 1);
            int offset = ((int)day - (int)first.DayOfWeek + 7) % 7;
            return first.AddDays(offset + (occurrence - 1) * 7);
        }

        private static DateTime LastWeekday(int year, int month, DayOfWeek day)
        {
            DateTime last = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            int offset = ((int)last.DayOfWeek - (int)day + 7) % 7;
            return last.AddDays(-offset);
        }

        private static void Add(Dictionary<DateTime, List<HolidayInfo>> result, int targetYear, DateTime date,
            string name, string shortName, HolidayRegion region)
        {
            if (date.Year != targetYear) return;
            List<HolidayInfo> list;
            if (!result.TryGetValue(date.Date, out list))
            {
                list = new List<HolidayInfo>();
                result[date.Date] = list;
            }
            list.Add(new HolidayInfo(name, shortName, region));
        }
    }

    internal sealed class CalendarForm : Form
    {
        private CalendarCanvas canvas;
        private Timer clock;
        private DateTime iconDate;
        private bool blurEnabled = true;

        public CalendarForm()
        {
            Text = "Calendar";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 520);
            Size = new Size(1080, 720);
            Font = (Font)SystemFonts.MessageBoxFont.Clone();
            KeyPreview = true;

            canvas = new CalendarCanvas();
            canvas.Dock = DockStyle.Fill;
            canvas.BlurToggleRequested += OnBlurToggleRequested;
            Controls.Add(canvas);

            iconDate = DateTime.Today;
            Icon = CalendarIcon.Create(iconDate, Theme.Accent, 64);
            clock = new Timer();
            clock.Interval = 30000;
            clock.Tick += delegate
            {
                if (DateTime.Today != iconDate)
                {
                    iconDate = DateTime.Today;
                    Icon old = Icon;
                    Icon = CalendarIcon.Create(iconDate, Theme.Accent, 64);
                    if (old != null) old.Dispose();
                    canvas.RefreshToday();
                }
            };
            clock.Start();
            SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyBackdrop();
        }

        private void OnBlurToggleRequested(object sender, EventArgs e)
        {
            blurEnabled = !blurEnabled;
            ApplyBackdrop();
        }

        private void ApplyBackdrop()
        {
            if (!IsHandleCreated || canvas == null) return;
            bool active = blurEnabled && WindowBackdrop.Apply(Handle, true, Theme.IsDark);
            if (!blurEnabled) WindowBackdrop.Apply(Handle, false, Theme.IsDark);
            BackColor = active ? Color.Black
                : Theme.IsDark ? Color.FromArgb(32, 32, 32) : Color.White;
            canvas.SetBlurEnabled(active);
            Invalidate(true);
        }

        private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate
            {
                Icon old = Icon;
                Icon = CalendarIcon.Create(DateTime.Today, Theme.Accent, 64);
                if (old != null) old.Dispose();
                canvas.ApplyTheme();
                ApplyBackdrop();
            });
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
            canvas.BlurToggleRequested -= OnBlurToggleRequested;
            clock.Dispose();
            base.OnFormClosed(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (canvas.HandleKey(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    internal sealed class CalendarCanvas : Control
    {
        private DateTime shownMonth;
        private DateTime selectedDate;
        private bool sidebarVisible = true;
        private bool blurEnabled = true;
        private bool menuVisible;
        private Rectangle hamburgerRect, todayRect, previousRect, nextRect;
        private Rectangle menuRect, menuBlurRect, menuSidebarRect;
        private Rectangle[,] dayRects = new Rectangle[6, 7];
        private Rectangle[,] previousMiniRects = new Rectangle[6, 7];
        private Rectangle[,] nextMiniRects = new Rectangle[6, 7];
        private Color background, panel, text, secondary, grid, hover, accent, accentText;
        private Color controlSurface, controlBorder, weekendSurface;
        private Color sidebarText, sidebarSecondary, sidebarSelection;
        private bool isDarkTheme;
        private int hoverRow = -1, hoverCol = -1;
        private DateTime? toolTipDate;
        private ToolTip holidayToolTip;
        private readonly string[] week = { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };

        public event EventHandler BlurToggleRequested;

        public CalendarCanvas()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            DoubleBuffered = true;
            ResizeRedraw = true;
            Cursor = Cursors.Default;
            TabStop = true;
            holidayToolTip = new ToolTip
            {
                AutomaticDelay = 250,
                AutoPopDelay = 8000,
                ReshowDelay = 50,
                ShowAlways = true
            };
            selectedDate = DateTime.Today;
            shownMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
            ApplyTheme();
        }

        public void SetBlurEnabled(bool enabled)
        {
            blurEnabled = enabled;
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            bool dark = Theme.IsDark;
            isDarkTheme = dark;
            background = blurEnabled
                ? (dark ? Color.FromArgb(98, 18, 18, 18) : Color.FromArgb(168, 255, 255, 255))
                : (dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(255, 255, 255));
            panel = blurEnabled
                ? Color.FromArgb(210, 14, 14, 17)
                : Color.FromArgb(16, 16, 19);
            text = dark ? Color.FromArgb(245, 245, 245) : Color.FromArgb(32, 32, 32);
            secondary = dark ? Color.FromArgb(188, 188, 188) : Color.FromArgb(88, 88, 88);
            grid = blurEnabled
                ? (dark ? Color.FromArgb(54, 255, 255, 255) : Color.FromArgb(42, 32, 32, 32))
                : (dark ? Color.FromArgb(61, 61, 61) : Color.FromArgb(225, 225, 225));
            hover = blurEnabled
                ? (dark ? Color.FromArgb(36, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0))
                : (dark ? Color.FromArgb(55, 55, 55) : Color.FromArgb(244, 244, 244));
            controlSurface = blurEnabled
                ? (dark ? Color.FromArgb(44, 255, 255, 255) : Color.FromArgb(168, 255, 255, 255))
                : (dark ? Color.FromArgb(48, 48, 48) : Color.FromArgb(250, 250, 250));
            controlBorder = dark ? Color.FromArgb(74, 255, 255, 255) : Color.FromArgb(50, 32, 32, 32);
            weekendSurface = dark ? Color.FromArgb(11, 255, 255, 255) : Color.FromArgb(8, 25, 90, 160);
            accent = Theme.Accent;
            accentText = accent.GetBrightness() > .62f ? Color.Black : Color.White;
            sidebarText = Color.White;
            sidebarSecondary = Color.FromArgb(205, 220, 220, 222);
            sidebarSelection = Color.FromArgb(96, 96, 102);
            BackColor = blurEnabled ? Color.Transparent
                : dark ? Color.FromArgb(32, 32, 32) : Color.White;
            ForeColor = text;
            Invalidate();
        }

        public void RefreshToday()
        {
            if (selectedDate.Date < DateTime.Today.Date.AddYears(-20)) selectedDate = DateTime.Today;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // ClearType assumes an opaque RGB background. On acrylic it creates
            // colored, smeared edges, so use grayscale antialiasing instead.
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            int side = sidebarVisible && Width >= 760 ? Math.Min(252, Math.Max(210, Width / 4)) : 0;
            // Under Windows 10 blur-behind, black pixels reveal the backdrop while
            // lighter pixels act as a frosted tint. These alpha fills become a soft
            // white glass layer without hiding the blurred app behind the calendar.
            using (SolidBrush b = new SolidBrush(background)) g.FillRectangle(b, ClientRectangle);
            if (side > 0)
            {
                if (blurEnabled)
                {
                    Rectangle panelRect = new Rectangle(0, 0, side, Height);
                    using (LinearGradientBrush b = new LinearGradientBrush(panelRect,
                        Color.FromArgb(205, 24, 24, 28), Color.FromArgb(224, 4, 4, 7),
                        LinearGradientMode.Vertical))
                        g.FillRectangle(b, panelRect);
                }
                else
                    using (SolidBrush b = new SolidBrush(panel)) g.FillRectangle(b, 0, 0, side, Height);
            }

            DrawSidebar(g, side);
            DrawMain(g, side);
            if (menuVisible) DrawHamburgerMenu(g);
        }

        private void DrawSidebar(Graphics g, int side)
        {
            hamburgerRect = new Rectangle(16, 16, 38, 38);
            DrawHoverButton(g, hamburgerRect, menuVisible);
            using (Pen p = new Pen(side > 0 ? sidebarText : text, 1.5f))
                for (int i = 0; i < 3; i++) g.DrawLine(p, 27, 27 + i * 6, 43, 27 + i * 6);
            if (side == 0)
            {
                previousMiniRects = new Rectangle[6, 7];
                nextMiniRects = new Rectangle[6, 7];
                return;
            }

            int cellH = Height >= 620 ? 24 : menuVisible ? 17 : 21;
            int miniHeight = 28 + 18 + 6 * cellH;
            int previousTop = menuVisible ? (Height >= 620 ? 170 : 160) : 76;
            int nextTop = previousTop + miniHeight + 22;
            DrawMiniMonth(g, side, shownMonth.AddMonths(-1), previousTop, cellH, previousMiniRects);
            using (Pen p = new Pen(Color.FromArgb(72, 255, 255, 255)))
                g.DrawLine(p, 18, previousTop + miniHeight + 10, side - 18, previousTop + miniHeight + 10);
            DrawMiniMonth(g, side, shownMonth.AddMonths(1), nextTop, cellH, nextMiniRects);
        }

        private void DrawMiniMonth(Graphics g, int side, DateTime month, int top, int cellH, Rectangle[,] rects)
        {
            int pad = 22;
            using (Font f = UiFont(13F, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(sidebarText))
                g.DrawString(month.ToString("MMMM yyyy", CultureInfo.CurrentCulture), f, b, pad, top);

            int miniTop = top + 28;
            int cellW = Math.Max(22, (side - pad * 2) / 7);
            using (Font f = UiFont(8F, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(sidebarSecondary))
                for (int c = 0; c < 7; c++)
                    DrawCentered(g, week[c].Substring(0, 1), f, b,
                        new Rectangle(pad + c * cellW, miniTop, cellW, 18));

            DateTime first = FirstGridDate(month);
            using (Font f = UiFont(8F, FontStyle.Regular))
            using (Font holidayDayFont = UiFont(8F, FontStyle.Bold))
            {
                for (int r = 0; r < 6; r++)
                for (int c = 0; c < 7; c++)
                {
                    DateTime date = first.AddDays(r * 7 + c);
                    Rectangle rect = new Rectangle(pad + c * cellW, miniTop + 18 + r * cellH, cellW, cellH);
                    rects[r, c] = rect;
                    bool isToday = date.Date == DateTime.Today;
                    bool isSelected = date.Date == selectedDate.Date;
                    if (isSelected)
                    {
                        Rectangle dot = CenterSquare(rect, Math.Min(23, cellH));
                        using (SolidBrush b = new SolidBrush(sidebarSelection)) g.FillEllipse(b, dot);
                    }
                    else if (isToday)
                    {
                        Rectangle dot = CenterSquare(rect, Math.Min(22, cellH));
                        using (Pen p = new Pen(sidebarText, 1.5f)) g.DrawEllipse(p, dot);
                    }
                    IList<HolidayInfo> holidays = HolidayCalendar.Get(date);
                    Color dc = date.Month == month.Month ? sidebarText : Color.FromArgb(120, sidebarSecondary);
                    Color holidayDate = Color.FromArgb(255, 104, 112);
                    Color dayColor = isSelected ? Color.White : holidays.Count > 0 ? holidayDate : dc;
                    Font dayFont = holidays.Count > 0 ? holidayDayFont : f;
                    using (SolidBrush b = new SolidBrush(dayColor)) DrawCentered(g, date.Day.ToString(), dayFont, b, rect);
                    for (int h = 0; h < Math.Min(2, holidays.Count); h++)
                    {
                        Color marker = HolidayColor(holidays[h]);
                        using (SolidBrush b = new SolidBrush(marker))
                            g.FillEllipse(b, rect.Left + rect.Width / 2 - 5 + h * 6, rect.Bottom - 5, 4, 4);
                    }
                }
            }
        }

        private void DrawMain(Graphics g, int side)
        {
            int left = side;
            // Keep the title clear of the hamburger even when the sidebar is hidden.
            int x = side > 0 ? left + 28 : 72;
            int contentW = Math.Max(200, Width - left - 28);
            using (Font f = UiFont(23F, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(text))
                g.DrawString(shownMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture), f, b, x, 19);

            int toolbarY = 68;
            todayRect = new Rectangle(x, toolbarY, 76, 36);
            previousRect = new Rectangle(x + 86, toolbarY, 36, 36);
            nextRect = new Rectangle(x + 130, toolbarY, 36, 36);
            DrawOutlineButton(g, todayRect, "Today", UiFont(9F, FontStyle.Regular));
            DrawGlyphButton(g, previousRect, "\uE70E");
            DrawGlyphButton(g, nextRect, "\uE70D");
            using (Font f = UiFont(9F, FontStyle.Regular))
            using (SolidBrush b = new SolidBrush(secondary))
                g.DrawString("Month", f, b, Width - 78, toolbarY + 10);

            int headerTop = 119;
            int available = Width - x - 18;
            int colW = available / 7;
            int rowH = Math.Max(48, (Height - headerTop - 31) / 6);
            using (Font f = UiFont(9.25F, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(secondary))
                for (int c = 0; c < 7; c++)
                    DrawCentered(g, week[c], f, b, new Rectangle(x + c * colW, headerTop, colW, 28));

            int gridTop = headerTop + 31;
            DateTime first = FirstGridDate(shownMonth);
            using (Font numberFont = UiFont(10F, FontStyle.Regular))
            using (Font todayFont = UiFont(10F, FontStyle.Bold))
            using (Font holidayDateFont = UiFont(10F, FontStyle.Bold))
            using (Font holidayFont = UiFont(8.5F, FontStyle.Regular))
            {
                for (int r = 0; r < 6; r++)
                for (int c = 0; c < 7; c++)
                {
                    DateTime date = first.AddDays(r * 7 + c);
                    Rectangle rect = new Rectangle(x + c * colW, gridTop + r * rowH, c == 6 ? available - c * colW : colW, rowH);
                    dayRects[r, c] = rect;
                    IList<HolidayInfo> holidays = HolidayCalendar.Get(date);
                    if (c >= 5)
                        using (SolidBrush b = new SolidBrush(weekendSurface)) g.FillRectangle(b, rect);
                    if (holidays.Count > 0)
                    {
                        Color holidayColor = HolidayColor(holidays[0]);
                        Rectangle holidayBox = Rectangle.Inflate(rect, -3, -3);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(isDarkTheme ? 40 : 34, holidayColor)))
                            g.FillRoundedRectangle(b, holidayBox, 8);
                        using (Pen p = new Pen(Color.FromArgb(isDarkTheme ? 116 : 98, holidayColor), 1.2F))
                            g.DrawRoundedRectangle(p, holidayBox, 8);
                    }
                    if (r == hoverRow && c == hoverCol)
                        using (SolidBrush b = new SolidBrush(hover))
                            g.FillRoundedRectangle(b, Rectangle.Inflate(rect, -3, -3), 7);
                    using (Pen p = new Pen(grid))
                    {
                        g.DrawLine(p, rect.Left, rect.Top, rect.Right, rect.Top);
                        g.DrawLine(p, rect.Left, rect.Top, rect.Left, rect.Bottom);
                    }

                    bool isToday = date.Date == DateTime.Today;
                    bool isSelected = date.Date == selectedDate.Date;
                    Rectangle numberRect = new Rectangle(rect.Left + 7, rect.Top + 7, 30, 30);
                    Color holidayDateColor = isDarkTheme ? Color.FromArgb(255, 102, 110) : Color.FromArgb(138, 0, 0);
                    if (isSelected)
                    {
                        Color selectedFill = holidays.Count > 0 ? holidayDateColor : accent;
                        using (SolidBrush b = new SolidBrush(selectedFill)) g.FillEllipse(b, numberRect);
                    }
                    else if (isToday)
                    {
                        using (Pen p = new Pen(accent, 2F)) g.DrawEllipse(p, numberRect);
                    }
                    Color dc = date.Month == shownMonth.Month ? text : Color.FromArgb(115, secondary);
                    Color numberColor = isSelected ? accentText : holidays.Count > 0 ? holidayDateColor : dc;
                    Font dateFont = holidays.Count > 0 ? holidayDateFont : isToday ? todayFont : numberFont;
                    using (SolidBrush b = new SolidBrush(numberColor))
                        DrawCentered(g, date.Day.ToString(), dateFont, b, numberRect);
                    DrawHolidayLabels(g, rect, holidays, holidayFont);
                }
            }
        }

        private void DrawHamburgerMenu(Graphics g)
        {
            menuRect = new Rectangle(14, 58, 218, 96);
            menuBlurRect = new Rectangle(menuRect.Left + 7, menuRect.Top + 8, menuRect.Width - 14, 36);
            menuSidebarRect = new Rectangle(menuRect.Left + 7, menuRect.Top + 49, menuRect.Width - 14, 36);

            bool darkSidebarMenu = sidebarVisible && Width >= 760;
            Color surface = darkSidebarMenu ? Color.FromArgb(255, 30, 30, 34)
                : isDarkTheme ? Color.FromArgb(255, 44, 44, 44) : Color.FromArgb(255, 250, 250, 250);
            Color menuText = darkSidebarMenu ? Color.White : text;
            Color border = darkSidebarMenu ? Color.FromArgb(110, 255, 255, 255)
                : isDarkTheme ? Color.FromArgb(92, 255, 255, 255) : Color.FromArgb(52, 32, 32, 32);
            using (SolidBrush b = new SolidBrush(surface)) g.FillRoundedRectangle(b, menuRect, 8);
            using (Pen p = new Pen(border)) g.DrawRoundedRectangle(p, menuRect, 8);

            using (SolidBrush b = new SolidBrush(darkSidebarMenu ? Color.FromArgb(34, 255, 255, 255) : hover))
                g.FillRoundedRectangle(b, menuBlurRect, 6);
            using (Font f = UiFont(9.5F, FontStyle.Regular))
            using (SolidBrush b = new SolidBrush(menuText))
            {
                g.DrawString("Blur background", f, b, menuBlurRect.Left + 12, menuBlurRect.Top + 8);
                string state = blurEnabled ? "On" : "Off";
                SizeF size = g.MeasureString(state, f);
                g.DrawString(state, f, b, menuBlurRect.Right - size.Width - 12, menuBlurRect.Top + 8);
                g.DrawString(sidebarVisible ? "Hide sidebar" : "Show sidebar", f, b,
                    menuSidebarRect.Left + 12, menuSidebarRect.Top + 8);
            }
            using (SolidBrush b = new SolidBrush(blurEnabled ? accent : secondary))
                g.FillEllipse(b, menuBlurRect.Right - 46, menuBlurRect.Top + 15, 7, 7);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int nr = -1, nc = -1;
            DateTime? dateUnderPointer = null;
            DateTime first = FirstGridDate(shownMonth);
            DateTime previousFirst = FirstGridDate(shownMonth.AddMonths(-1));
            DateTime nextFirst = FirstGridDate(shownMonth.AddMonths(1));
            for (int r = 0; r < 6; r++)
                for (int c = 0; c < 7; c++)
                {
                    if (dayRects[r, c].Contains(e.Location))
                    {
                        nr = r;
                        nc = c;
                        dateUnderPointer = first.AddDays(r * 7 + c);
                    }
                    else if (previousMiniRects[r, c].Contains(e.Location))
                        dateUnderPointer = previousFirst.AddDays(r * 7 + c);
                    else if (nextMiniRects[r, c].Contains(e.Location))
                        dateUnderPointer = nextFirst.AddDays(r * 7 + c);
                }
            if (nr != hoverRow || nc != hoverCol) { hoverRow = nr; hoverCol = nc; Invalidate(); }
            if (dateUnderPointer != toolTipDate)
            {
                toolTipDate = dateUnderPointer;
                holidayToolTip.SetToolTip(this, dateUnderPointer.HasValue
                    ? HolidayToolTip(dateUnderPointer.Value) : String.Empty);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverRow = hoverCol = -1;
            toolTipDate = null;
            holidayToolTip.SetToolTip(this, String.Empty);
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (hamburgerRect.Contains(e.Location))
            {
                menuVisible = !menuVisible;
                Invalidate();
                return;
            }
            if (menuVisible)
            {
                if (menuBlurRect.Contains(e.Location))
                {
                    menuVisible = false;
                    EventHandler handler = BlurToggleRequested;
                    if (handler != null) handler(this, EventArgs.Empty);
                    return;
                }
                if (menuSidebarRect.Contains(e.Location))
                {
                    sidebarVisible = !sidebarVisible;
                    menuVisible = false;
                    Invalidate();
                    return;
                }
                menuVisible = false;
                Invalidate();
                return;
            }
            if (todayRect.Contains(e.Location)) { GoToday(); return; }
            if (previousRect.Contains(e.Location)) { ChangeMonth(-1); return; }
            if (nextRect.Contains(e.Location)) { ChangeMonth(1); return; }
            DateTime first = FirstGridDate(shownMonth);
            for (int r = 0; r < 6; r++)
            for (int c = 0; c < 7; c++)
            {
                if (dayRects[r, c].Contains(e.Location))
                {
                    selectedDate = first.AddDays(r * 7 + c);
                    shownMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
                    Invalidate();
                    return;
                }
                if (previousMiniRects[r, c].Contains(e.Location))
                {
                    selectedDate = FirstGridDate(shownMonth.AddMonths(-1)).AddDays(r * 7 + c);
                    shownMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
                    Invalidate();
                    return;
                }
                if (nextMiniRects[r, c].Contains(e.Location))
                {
                    selectedDate = FirstGridDate(shownMonth.AddMonths(1)).AddDays(r * 7 + c);
                    shownMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
                    Invalidate();
                    return;
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ChangeMonth(e.Delta > 0 ? -1 : 1);
            base.OnMouseWheel(e);
        }

        public bool HandleKey(Keys key)
        {
            int days = 0;
            if (key == (Keys.Control | Keys.B))
            {
                EventHandler handler = BlurToggleRequested;
                if (handler != null) handler(this, EventArgs.Empty);
                return true;
            }
            if (key == Keys.Left) days = -1;
            else if (key == Keys.Right) days = 1;
            else if (key == Keys.Up) days = -7;
            else if (key == Keys.Down) days = 7;
            else if (key == Keys.PageUp) { ChangeMonth(-1); return true; }
            else if (key == Keys.PageDown) { ChangeMonth(1); return true; }
            else if (key == Keys.Home) { GoToday(); return true; }
            else return false;
            selectedDate = selectedDate.AddDays(days);
            shownMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
            Invalidate();
            return true;
        }

        private void ChangeMonth(int offset)
        {
            shownMonth = shownMonth.AddMonths(offset);
            int day = Math.Min(selectedDate.Day, DateTime.DaysInMonth(shownMonth.Year, shownMonth.Month));
            selectedDate = new DateTime(shownMonth.Year, shownMonth.Month, day);
            Invalidate();
        }

        private void GoToday()
        {
            selectedDate = DateTime.Today;
            shownMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
            Invalidate();
        }

        private static DateTime FirstGridDate(DateTime month)
        {
            int mondayOffset = ((int)month.DayOfWeek + 6) % 7;
            return month.AddDays(-mondayOffset);
        }

        private void DrawOutlineButton(Graphics g, Rectangle r, string label, Font font)
        {
            using (SolidBrush b = new SolidBrush(controlSurface)) g.FillRoundedRectangle(b, r, 7);
            using (Pen p = new Pen(controlBorder)) g.DrawRoundedRectangle(p, r, 7);
            using (SolidBrush b = new SolidBrush(text)) DrawCentered(g, label, font, b, r);
            font.Dispose();
        }

        private void DrawModeButton(Graphics g, Rectangle r, string label, bool active)
        {
            Color fill = active ? Color.FromArgb(isDarkTheme ? 52 : 34, accent) : controlSurface;
            using (SolidBrush b = new SolidBrush(fill)) g.FillRoundedRectangle(b, r, 7);
            using (Pen p = new Pen(active ? accent : controlBorder, active ? 1.2F : 1F))
                g.DrawRoundedRectangle(p, r, 7);
            using (Font f = UiFont(9F, FontStyle.Regular))
            using (SolidBrush b = new SolidBrush(text)) DrawCentered(g, label, f, b, r);
        }

        private void DrawHolidayLabels(Graphics g, Rectangle dayRect, IList<HolidayInfo> holidays, Font font)
        {
            if (holidays.Count == 0 || dayRect.Height < 56) return;
            int lines = dayRect.Height >= 82 ? Math.Min(2, holidays.Count) : 1;
            int top = dayRect.Top + 43;
            for (int i = 0; i < lines; i++)
            {
                HolidayInfo holiday = holidays[i];
                Color marker = HolidayColor(holiday);
                Rectangle labelRect = new Rectangle(dayRect.Left + 6, top + i * 22,
                    Math.Max(12, dayRect.Width - 12), 20);
                Color card = isDarkTheme
                    ? Mix(marker, Color.FromArgb(38, 38, 38), .78F)
                    : Mix(marker, Color.White, .88F);
                Color cardBorder = Color.FromArgb(isDarkTheme ? 145 : 112, marker);
                using (SolidBrush b = new SolidBrush(card)) g.FillRoundedRectangle(b, labelRect, 6);
                using (Pen p = new Pen(cardBorder)) g.DrawRoundedRectangle(p, labelRect, 6);
                Rectangle markerRect = new Rectangle(labelRect.Left + 7, labelRect.Top + 7, 6, 6);
                using (SolidBrush b = new SolidBrush(marker)) g.FillEllipse(b, markerRect);
                string label = holiday.ShortName;
                if (i == lines - 1 && holidays.Count > lines) label += " +" + (holidays.Count - lines);
                Rectangle textRect = new Rectangle(labelRect.Left + 18, labelRect.Top,
                    Math.Max(4, labelRect.Width - 22), labelRect.Height);
                Color labelText = isDarkTheme ? Color.FromArgb(245, 245, 245) : Color.FromArgb(42, 42, 42);
                using (SolidBrush b = new SolidBrush(labelText))
                using (StringFormat format = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                })
                    g.DrawString(label, font, b, textRect, format);
            }
        }

        private static Color HolidayColor(HolidayInfo holiday)
        {
            Color[] palette =
            {
                Color.FromArgb(214, 68, 68),
                Color.FromArgb(221, 116, 20),
                Color.FromArgb(124, 150, 22),
                Color.FromArgb(0, 145, 137),
                Color.FromArgb(38, 114, 201),
                Color.FromArgb(120, 81, 190),
                Color.FromArgb(194, 64, 126)
            };
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < holiday.Name.Length; i++) hash = hash * 31 + holiday.Name[i];
                return palette[(hash & Int32.MaxValue) % palette.Length];
            }
        }

        private static Color Mix(Color first, Color second, float secondWeight)
        {
            float firstWeight = 1F - secondWeight;
            return Color.FromArgb(
                (int)(first.R * firstWeight + second.R * secondWeight),
                (int)(first.G * firstWeight + second.G * secondWeight),
                (int)(first.B * firstWeight + second.B * secondWeight));
        }

        private static string HolidayToolTip(DateTime date)
        {
            IList<HolidayInfo> holidays = HolidayCalendar.Get(date);
            if (holidays.Count == 0) return String.Empty;
            string result = date.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture);
            for (int i = 0; i < holidays.Count; i++)
            {
                string region = holidays[i].Region == HolidayRegion.India ? "India" : "USA";
                result += Environment.NewLine + region + " — " + holidays[i].Name;
            }
            return result;
        }

        private void DrawGlyphButton(Graphics g, Rectangle r, string glyph)
        {
            using (SolidBrush b = new SolidBrush(controlSurface)) g.FillRoundedRectangle(b, r, 7);
            using (Pen p = new Pen(controlBorder)) g.DrawRoundedRectangle(p, r, 7);
            using (Font f = new Font("Segoe MDL2 Assets", 10F))
            using (SolidBrush b = new SolidBrush(text)) DrawCentered(g, glyph, f, b, r);
        }

        private void DrawHoverButton(Graphics g, Rectangle r, bool active)
        {
            if (active) using (SolidBrush b = new SolidBrush(hover)) g.FillRectangle(b, r);
        }

        private static void DrawCentered(Graphics g, string value, Font font, Brush brush, Rectangle rect)
        {
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(value, font, brush, rect, sf);
        }

        private static Font UiFont(float size, FontStyle style)
        {
            return new Font(SystemFonts.MessageBoxFont.FontFamily, size, style, GraphicsUnit.Point);
        }

        private static Rectangle CenterSquare(Rectangle rect, int size)
        {
            return new Rectangle(rect.Left + (rect.Width - size) / 2, rect.Top + (rect.Height - size) / 2, size, size);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && holidayToolTip != null) holidayToolTip.Dispose();
            base.Dispose(disposing);
        }
    }

    internal static class CalendarIcon
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon Create(DateTime date, Color accent, int size)
        {
            using (Bitmap bitmap = new Bitmap(size, size))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                Rectangle body = new Rectangle(5, 7, size - 10, size - 12);
                using (SolidBrush white = new SolidBrush(Color.White)) g.FillRoundedRectangle(white, body, 7);
                using (Pen border = new Pen(Color.FromArgb(80, 80, 80), Math.Max(1, size / 32f))) g.DrawRoundedRectangle(border, body, 7);
                using (SolidBrush strip = new SolidBrush(accent)) g.FillRectangle(strip, 6, 8, size - 12, Math.Max(7, size / 6));
                using (Font f = new Font("Segoe UI Semibold", size * .35f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush dark = new SolidBrush(Color.FromArgb(35, 35, 35)))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(date.Day.ToString(), f, dark, new Rectangle(4, size / 5, size - 8, size - size / 5 - 2), sf);
                IntPtr handle = bitmap.GetHicon();
                try { using (Icon temp = Icon.FromHandle(handle)) return (Icon)temp.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        internal static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rect, int radius)
        {
            using (GraphicsPath path = Rounded(rect, radius)) g.FillPath(brush, path);
        }

        internal static void DrawRoundedRectangle(this Graphics g, Pen pen, Rectangle rect, int radius)
        {
            using (GraphicsPath path = Rounded(rect, radius)) g.DrawPath(pen, path);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
