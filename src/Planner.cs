using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Xiaohei
{
    internal sealed partial class Planner : Window
    {
        internal event Action<bool> TaskCompleted;
        private const double TitleSize = 16, HeadingSize = 14, BodySize = 13, ControlSize = 12, MetaSize = 11.5;
        private static readonly Brush Ink = Paint("#23362C"), Muted = Paint("#54665B");
        private static readonly Brush Leaf = Paint("#365D45"), Sage = Paint("#E5EDE1");
        private static readonly Brush Paper = Paint("#FCFDF9"), Line = Paint("#D9E1D6");
        private static readonly Brush Header = Paint("#EAF1E5"), TodayRing = Paint("#81A081");
        private static readonly Brush Alert = Paint("#B64C4C");
        private static readonly Style TaskSurface = (Style)XamlReader.Parse(@"
            <Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Border'>
                <Setter Property='CornerRadius' Value='7'/>
                <Setter Property='Background' Value='Transparent'/>
                <Style.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'>
                        <Setter Property='Background' Value='#E1E9DE'/>
                    </Trigger>
                    <Trigger Property='IsKeyboardFocusWithin' Value='True'>
                        <Setter Property='Background' Value='#CBD9C8'/>
                    </Trigger>
                </Style.Triggers>
            </Style>");
        private static readonly ControlTemplate InputSkin = (ControlTemplate)XamlReader.Parse(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'>" +
            "<Border x:Name='field' CornerRadius='9' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'>" +
            "<ScrollViewer x:Name='PART_ContentHost' Focusable='False' HorizontalScrollBarVisibility='Hidden' VerticalScrollBarVisibility='Hidden'/></Border>" +
            "<ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='field' Property='BorderBrush' Value='#66866B'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        private static readonly ControlTemplate ProgressSkin = (ControlTemplate)XamlReader.Parse(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ProgressBar'>" +
            "<Grid><Border x:Name='PART_Track' Background='{TemplateBinding Background}' CornerRadius='3'/>" +
            "<Border x:Name='PART_Indicator' HorizontalAlignment='Left' Background='{TemplateBinding Foreground}' CornerRadius='3'/></Grid></ControlTemplate>");
        private static readonly string[] Days = { "一", "二", "三", "四", "五", "六", "日" };
        private static readonly CultureInfo Zh = CultureInfo.GetCultureInfo("zh-CN");
        private static readonly ControlTemplate ButtonSkin = (ControlTemplate)XamlReader.Parse(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
            "<Border x:Name='tile' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border>" +
            "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='tile' Property='BorderBrush' Value='#78947E'/></Trigger>" +
            "<Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='tile' Property='BorderBrush' Value='#267647'/></Trigger>" +
            "<Trigger Property='IsPressed' Value='True'><Setter TargetName='tile' Property='Opacity' Value='.75'/></Trigger>" +
            "</ControlTemplate.Triggers></ControlTemplate>");
        private static readonly ControlTemplate CheckSkin = (ControlTemplate)XamlReader.Parse(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='CheckBox'>" +
            "<StackPanel Orientation='Horizontal' Background='Transparent'><Grid Width='27' Height='28'><Border x:Name='halo' Width='25' Height='25' CornerRadius='9' Background='Transparent'/>" +
            "<Border x:Name='box' Width='18' Height='18' CornerRadius='6' Background='#FCFDF9' BorderBrush='#72907D' BorderThickness='1.5'/>" +
            "<Path x:Name='tick' Data='M 1,5 L 4,8 L 10,1' Stroke='White' StrokeThickness='2' StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round' HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Hidden'/></Grid><ContentPresenter VerticalAlignment='Center'/></StackPanel>" +
            "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='halo' Property='Background' Value='#E1E9DE'/><Setter TargetName='box' Property='BorderBrush' Value='#365D45'/><Setter TargetName='box' Property='Background' Value='#E5EDE1'/></Trigger>" +
            "<Trigger Property='IsChecked' Value='True'><Setter TargetName='box' Property='Background' Value='#365D45'/><Setter TargetName='box' Property='BorderBrush' Value='#365D45'/><Setter TargetName='tick' Property='Visibility' Value='Visible'/></Trigger>" +
            "<Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='halo' Property='Background' Value='#CBD9C8'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        private static readonly ControlTemplate GripSkin = (ControlTemplate)XamlReader.Parse(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Thumb'>" +
            "<Grid Background='Transparent'><Border x:Name='pad' Margin='0,0,4,3' Background='Transparent' BorderBrush='Transparent' BorderThickness='1' CornerRadius='6'><Path Data='M 2,10 L 10,2 M 7,10 L 10,7' Stroke='#647F69' StrokeThickness='1.8' StrokeStartLineCap='Round' StrokeEndLineCap='Round' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></Grid>" +
            "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='pad' Property='Background' Value='#E5EDE1'/></Trigger>" +
            "<Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='pad' Property='BorderBrush' Value='#365D45'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        private readonly Store store;
        private readonly StackPanel sections = new StackPanel();
        private readonly ScrollViewer scroll = new ScrollViewer();
        private readonly UniformGrid modes = new UniformGrid { Columns = 2 };
        private readonly List<UIElement> nextSections = new List<UIElement>();
        private readonly Border calendarShell = new Border { Margin = new Thickness(14, 0, 14, 2),
            BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 4, 0, 0) };
        private readonly DockPanel monthPanel = new DockPanel();
        private readonly DockPanel calendarGrid = new DockPanel();
        private readonly TaskCalendarBody body;
        private readonly StackPanel dateRows = new StackPanel();
        private readonly ScrollViewer weekScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
        private DateTime? calendarFocusDate;
        private double minimumWeekHeight, focusWeekTop, focusWeekHeight;
        private DateTime selected;
        private bool weekly;
        private FrameworkElement dayHeading, longTermHeading;

        internal Planner(Store store)
        {
            this.store = store;
            selected = store.Now.Date;
            Title = "小黑手帐";
            MinWidth = 300;
            MinHeight = 270;
            Width = Math.Max(MinWidth, store.PlannerWidth);
            Height = Math.Max(MinHeight, store.PlannerHeight);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = BodySize;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            UseLayoutRounding = true;
            Foreground = Ink;
            var frame = new DockPanel();
            var surface = new Grid();
            surface.Children.Add(RoundedSheet(frame));
            Content = surface;

            var masthead = new DockPanel { LastChildFill = true, Background = Header, Margin = new Thickness(0) };
            var top = new Border { Child = masthead, Background = Header, Padding = new Thickness(13, 6, 9, 6) };
            DockPanel.SetDock(top, Dock.Top);
            frame.Children.Add(top);
            var close = Button("×", delegate { Close(); }, Brushes.Transparent, Ink);
            close.FontSize = 19;
            close.Width = 32;
            close.Height = 32;
            close.Padding = new Thickness(0);
            Accessible(close, "收起手帐");
            DockPanel.SetDock(close, Dock.Right);
            masthead.Children.Add(close);
            var mark = new Viewbox { Width = 32, Height = 31, Child = PetArt.Create(false), Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(mark, Dock.Left);
            masthead.Children.Add(mark);
            masthead.Children.Add(Label("小黑手帐", TitleSize, Ink, true));

            var tabs = new Border { Padding = new Thickness(12, 3, 12, 0), BorderBrush = Line,
                BorderThickness = new Thickness(0, 0, 0, 1), Child = modes };
            DockPanel.SetDock(tabs, Dock.Top);
            frame.Children.Add(tabs);
            var footer = new Border { Height = 22 };
            surface.SizeChanged += delegate(object sender, SizeChangedEventArgs e) {
                if (e.HeightChanged) footer.Height = e.NewSize.Height <= 300 ? 8 : 22;
            };
            DockPanel.SetDock(footer, Dock.Bottom);
            frame.Children.Add(footer);
            InstallFrameGestures(top, close, AddResizeHandles(surface));
            sections.Margin = new Thickness(14, 8, 14, 4);
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.PanningMode = PanningMode.VerticalOnly;
            scroll.Content = sections;
            body = new TaskCalendarBody(this);
            body.Children.Add(scroll);
            body.Children.Add(calendarShell);
            frame.Children.Add(body);
            calendarShell.Child = monthPanel;
            var weekdays = new UniformGrid { Columns = 7 };
            foreach (var name in Days)
            {
                var weekday = Label(name, MetaSize, Muted);
                weekday.TextAlignment = TextAlignment.Center;
                weekday.Height = 22;
                weekdays.Children.Add(weekday);
            }
            DockPanel.SetDock(weekdays, Dock.Top);
            calendarGrid.Children.Add(weekdays);
            calendarGrid.Children.Add(weekScroll);
            weekScroll.Content = dateRows;
            AutomationProperties.SetName(scroll, "任务清单");
            AutomationProperties.SetName(weekScroll, "月历日期");
            AutomationProperties.SetHelpText(weekScroll, "滚动查看本月其他周");
            weekScroll.SizeChanged += delegate(object sender, SizeChangedEventArgs e) {
                if (e.HeightChanged && e.NewSize.Height > 0) RevealCalendarWeek(e.NewSize.Height);
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Escape || draftComposing) return;
                e.Handled = true;
                if (stepRow != null) CancelStep();
                else if (draftRow != null) CancelDraft();
                else Close();
            };
            Closing += delegate(object sender, CancelEventArgs e)
            {
                closingPlanner = true;
                if (!FinishOpenEditors()) { closingPlanner = false; e.Cancel = true; }
            };
            Deactivated += delegate { Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(DismissIfOutside)); };
            Render(true);
        }

        private static Border RoundedSheet(FrameworkElement child)
        {
            child.SizeChanged += delegate(object sender, SizeChangedEventArgs e)
            {
                child.Clip = new RectangleGeometry(new Rect(e.NewSize), 16, 16);
            };
            return new Border { BorderThickness = new Thickness(1), BorderBrush = TodayRing, Background = Paper,
                CornerRadius = new CornerRadius(17), Child = child };
        }

        private static Brush Paint(string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color);
            brush.Freeze();
            return brush;
        }

        private static TextBlock Label(string text, double size, Brush color, bool bold = false)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = color,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        }

        private static Button Button(string text, Action act, Brush background = null, Brush foreground = null)
        {
            var button = new Button { Content = text, Template = ButtonSkin, FontSize = ControlSize, MinHeight = 28,
                Foreground = foreground ?? Ink, Background = background ?? Brushes.Transparent, Cursor = Cursors.Hand,
                Padding = new Thickness(8, 2, 8, 2), BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent };
            button.Click += delegate { act(); };
            return button;
        }

        private static void Accessible(FrameworkElement element, string description)
        {
            AutomationProperties.SetName(element, description);
            element.ToolTip = description;
        }

        private static DateTime Monday(DateTime date)
        {
            return date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        }

        private static string DayKey(DateTime date) { return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        private static string DateLabel(DateTime date) { return date.ToString("M月d日 dddd", Zh); }

        internal void RefreshSchedule(DateTime previousTime)
        {
            if (selected == previousTime.Date) selected = store.Now.Date;
            Render(false);
        }

        private void Render(bool reset)
        {
            modes.Children.Clear();
            AddTab("日历计划", false);
            AddTab("每周汇总", true);
            nextSections.Clear();
            if (weekly) DrawWeek(); else DrawDay();
            // An open step's parent can leave this date at midnight. Keep the input with its
            // original task; keeping the element in the tree also preserves any IME composition.
            if (stepRow != null && !nextSections.Contains(stepRow))
            {
                nextSections.Add(Label("为「" + stepParent.Title + "」添加子任务", ControlSize, Muted));
                nextSections.Add(stepRow);
            }
            dayListEnd = !weekly ? nextSections.LastOrDefault() : null;
            calendarShell.Visibility = weekly ? Visibility.Collapsed : Visibility.Visible;
            if (!weekly) DrawMonth();
            for (int i = sections.Children.Count - 1; i >= 0; i--)
                if (sections.Children[i] != draftRow && sections.Children[i] != stepRow)
                    sections.Children.RemoveAt(i);
            for (int i = 0; i < nextSections.Count; i++)
                if (nextSections[i] != draftRow && nextSections[i] != stepRow || !sections.Children.Contains(nextSections[i]))
                    sections.Children.Insert(i, nextSections[i]);
            if (reset) scroll.ScrollToTop();
            RequestDayHeight();
        }

        private void AddTab(string text, bool week)
        {
            bool active = weekly == week;
            var tab = Button(text, delegate { if (weekly != week && FinishOpenEditors()) { weekly = week; Render(true); } },
                Brushes.Transparent, active ? Leaf : Muted);
            tab.FontSize = BodySize;
            tab.FontWeight = active ? FontWeights.Medium : FontWeights.Normal;
            tab.Height = 30;
            modes.Children.Add(new Border { Child = tab, BorderBrush = active ? Leaf : Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 2), Margin = new Thickness(4, 0, 4, 0) });
        }

        private void Go(DateTime date, bool keepCalendarInView = false)
        {
            bool dateEditor = draftRow != null && !draftLongTerm || stepParent != null && !stepParent.IsLongTerm;
            if (date != selected && (weekly || dateEditor) && !FinishOpenEditors()) return;
            bool revealDay = !weekly && !keepCalendarInView;
            if (revealDay && !SetListCollapsed(false, false)) { Render(false); return; }
            selected = date;
            calendarFocusDate = null;
            Render(!keepCalendarInView);
            if (revealDay && dayHeading != null)
            {
                scroll.UpdateLayout();
                scroll.ScrollToVerticalOffset(sections.Margin.Top + dayHeading.TranslatePoint(new Point(), sections).Y);
            }
        }

        private FrameworkElement Navigation(string title, bool week, FrameworkElement calendar = null)
        {
            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, week ? 9 : 2) };
            var nav = new StackPanel { Orientation = Orientation.Horizontal };
            var prev = Button("‹", delegate { Go(week ? selected.AddDays(-7) : selected.AddMonths(-1), !week); });
            var next = Button("›", delegate { Go(week ? selected.AddDays(7) : selected.AddMonths(1), !week); });
            prev.FontSize = next.FontSize = 17;
            prev.Width = next.Width = 28;
            prev.Padding = next.Padding = new Thickness(0);
            Accessible(prev, week ? "上一周" : "上个月");
            Accessible(next, week ? "下一周" : "下个月");
            nav.Children.Add(prev);
            nav.Children.Add(Button(week ? "本周" : "今天", delegate { Go(store.Now.Date); }, Sage, Leaf));
            nav.Children.Add(next);
            DockPanel.SetDock(nav, Dock.Right);
            line.Children.Add(nav);
            if (week) line.Children.Add(Label(title, HeadingSize, Ink, true));
            else line.Children.Add(MonthHeading(title, calendar, nav));
            return line;
        }

        private FrameworkElement MonthHeading(string title, FrameworkElement calendar, FrameworkElement nav)
        {
            Action updateVisibility = delegate {
                calendar.Visibility = nav.Visibility = store.CalendarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            };
            updateVisibility();
            return DisclosureHeading(title, HeadingSize, () => store.CalendarCollapsed, "月历", delegate {
                store.CalendarCollapsed = !store.CalendarCollapsed;
                if (!Persist(this)) store.CalendarCollapsed = !store.CalendarCollapsed;
                updateVisibility();
                if (!store.CalendarCollapsed) weekScroll.ScrollToVerticalOffset(focusWeekTop);
                RequestDayHeight();
            }, selected.ToString("dddd", Zh));
        }

        private static FrameworkElement DisclosureHeading(string title, double fontSize, Func<bool> isFolded,
            string name, Action toggle, string weekday = null)
        {
            var caption = new StackPanel { Orientation = Orientation.Horizontal };
            caption.Children.Add(Label(title, fontSize, Ink, true));
            FrameworkElement content = caption;
            if (weekday != null)
            {
                var day = Label(weekday, MetaSize, Muted);
                day.Margin = new Thickness(5, 0, 0, 0);
                caption.Children.Add(day);
                content = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = caption };
            }
            var arrow = new System.Windows.Shapes.Path { Width = 9, Height = 5, Stretch = Stretch.Fill,
                Data = Geometry.Parse("M 1,1 L 5,5 L 9,1"), Stroke = Leaf, StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                RenderTransformOrigin = new Point(.5,.5), Margin = new Thickness(6, 0, 1, 0) };
            caption.Children.Add(arrow);
            var button = new Button { Content = content, Template = ButtonSkin, Background = Brushes.Transparent,
                Padding = new Thickness(2, 2, 3, 2), BorderThickness = new Thickness(0), MinHeight = 28,
                FocusVisualStyle = null, Cursor = Cursors.Hand };
            if (weekday != null) AutomationProperties.SetHelpText(button, title + " " + weekday);
            Action update = delegate {
                bool folded = isFolded();
                arrow.RenderTransform = folded ? Transform.Identity : new RotateTransform(180);
                // Describe the action to assistive technology; keep the visible control icon-only.
                AutomationProperties.SetName(button, (folded ? "展开" : "收起") + name);
            };
            button.Click += delegate { toggle(); update(); };
            update();
            return new Border { Style = TaskSurface, HorizontalAlignment = HorizontalAlignment.Left, Child = button };
        }

        private void DrawMonth()
        {
            monthPanel.Children.Clear();
            var nav = Navigation(selected.ToString("yyyy年M月d日", Zh), false, calendarGrid);
            DockPanel.SetDock(nav, Dock.Top);
            monthPanel.Children.Add(nav);
            monthPanel.Children.Add(calendarGrid);
            dateRows.Children.Clear();
            UniformGrid week = null;
            var month = new DateTime(selected.Year, selected.Month, 1);
            var first = Monday(month);
            int cells = (int)Math.Ceiling(((month - first).Days + DateTime.DaysInMonth(month.Year, month.Month)) / 7.0) * 7;
            cells = Math.Min(cells, (DateTime.MaxValue.Date - first).Days + 1);
            minimumWeekHeight = focusWeekHeight = 40;
            focusWeekTop = (selected - first).Days / 7 * focusWeekHeight;
            var now = store.Now;
            for (int i = 0; i < cells; i++)
            {
                var date = first.AddDays(i);
                if (i % 7 == 0)
                {
                    week = new UniformGrid { Columns = 7, Height = minimumWeekHeight };
                    dateRows.Children.Add(week);
                }
                bool chosen = date == selected, today = date == now.Date;
                bool hasTasks = store.Tasks.Any(t => store.IsOnDay(t, date, now));
                string mark = HolidayCalendar.IsHoliday(date) ? "休" : HolidayCalendar.IsMakeupWorkday(date) ? "班" : null;
                string festival = HolidayCalendar.FestivalName(date);
                string lunar = HolidayCalendar.LunarLabel(date);
                string caption = festival ?? lunar;
                var button = Button("", delegate { Go(date); }, chosen ? Leaf : Brushes.Transparent);
                button.Margin = new Thickness(2, 1, 2, 1);
                button.MinHeight = 24;
                button.Height = week.Height - 2;
                button.Padding = new Thickness(0);
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                if (today && !chosen) button.BorderBrush = TodayRing;
                var color = chosen ? Brushes.White : date.Month == selected.Month ? Ink : Muted;
                var content = new Grid { Height = button.Height - 4 };
                var number = Label(date.Day.ToString(), BodySize, color, chosen || today);
                number.TextAlignment = TextAlignment.Center;
                if (caption != null)
                {
                    number.VerticalAlignment = VerticalAlignment.Top;
                    number.Margin = new Thickness(0, 1, 0, 0);
                }
                content.Children.Add(number);
                if (mark != null)
                {
                    number.Margin = new Thickness(0, number.Margin.Top, 6, 0);
                    var badge = Label(mark, 8, chosen ? Brushes.White : mark == "休" ? Leaf : Muted);
                    badge.Margin = new Thickness(0, 0, 1, 0);
                    badge.HorizontalAlignment = HorizontalAlignment.Right;
                    badge.VerticalAlignment = VerticalAlignment.Top;
                    content.Children.Add(badge);
                }
                if (caption != null)
                {
                    var label = Label(caption, festival == null ? 10 : 9, chosen ? Brushes.White : festival == null ? Muted : Leaf);
                    if (festival != null) label.FontWeight = FontWeights.Light;
                    label.HorizontalAlignment = HorizontalAlignment.Center;
                    label.VerticalAlignment = VerticalAlignment.Bottom;
                    label.Margin = new Thickness(0, 0, 0, 3);
                    content.Children.Add(label);
                }
                if (hasTasks)
                    content.Children.Add(new Border { Width = 8, Height = 2, CornerRadius = new CornerRadius(1),
                        Background = chosen ? Brushes.White : Leaf, VerticalAlignment = VerticalAlignment.Bottom });
                button.Content = content;
                Accessible(button, DateLabel(date) + (festival == null ? "" : "，" + festival) +
                    (mark == null ? "" : "，" + mark) + (hasTasks ? "，有任务" : ""));
                if (lunar != null) AutomationProperties.SetHelpText(button, "农历" + lunar);
                week.Children.Add(button);
            }
            if (calendarFocusDate != selected)
            {
                weekScroll.ScrollToVerticalOffset(focusWeekTop);
                calendarFocusDate = selected;
            }
        }

        private void RevealCalendarWeek(double viewport)
        {
            if (focusWeekTop < weekScroll.VerticalOffset)
                weekScroll.ScrollToVerticalOffset(focusWeekTop);
            else if (focusWeekTop + focusWeekHeight > weekScroll.VerticalOffset + viewport)
                weekScroll.ScrollToVerticalOffset(focusWeekTop + focusWeekHeight - viewport);
        }

        private sealed class TaskCalendarBody : Panel
        {
            private readonly Planner planner;
            internal double CalendarHeaderHeight, CalendarNaturalHeight;
            private double calendarHeight;
            internal TaskCalendarBody(Planner planner) { this.planner = planner; }

            protected override Size MeasureOverride(Size available)
            {
                var natural = new Size(available.Width, Double.PositiveInfinity);
                planner.sections.Measure(natural);
                CalendarHeaderHeight = CalendarNaturalHeight = calendarHeight = 0;
                if (!planner.weekly)
                {
                    var shell = planner.calendarShell;
                    var inner = new Size(Math.Max(0, available.Width - shell.Margin.Left - shell.Margin.Right), Double.PositiveInfinity);
                    var heading = planner.monthPanel.Children[0];
                    heading.Measure(inner);
                    CalendarHeaderHeight = heading.DesiredSize.Height + shell.Margin.Bottom + shell.BorderThickness.Top + shell.Padding.Top;
                    double minimum = 0, allWeeks = 0;
                    if (!planner.store.CalendarCollapsed)
                    {
                        var weekdays = planner.calendarGrid.Children[0];
                        weekdays.Measure(inner);
                        CalendarHeaderHeight += weekdays.DesiredSize.Height;
                        planner.dateRows.Measure(inner);
                        allWeeks = planner.dateRows.DesiredSize.Height;
                        minimum = planner.minimumWeekHeight;
                    }
                    CalendarNaturalHeight = CalendarHeaderHeight + allWeeks;
                    double remaining = available.Height - planner.sections.DesiredSize.Height;
                    calendarHeight = Math.Min(CalendarNaturalHeight, Math.Max(CalendarHeaderHeight + minimum, remaining));
                }
                planner.calendarShell.Measure(new Size(available.Width, calendarHeight));
                planner.scroll.Measure(new Size(available.Width, Math.Max(0, available.Height - calendarHeight)));
                return new Size(Math.Max(planner.scroll.DesiredSize.Width, planner.calendarShell.DesiredSize.Width),
                    planner.scroll.DesiredSize.Height + calendarHeight);
            }

            protected override Size ArrangeOverride(Size available)
            {
                double dateHeight = Math.Min(calendarHeight, available.Height);
                double tasks = Math.Min(planner.sections.DesiredSize.Height, available.Height - dateHeight);
                planner.scroll.Arrange(new Rect(0, 0, available.Width, tasks));
                planner.calendarShell.Arrange(new Rect(0, tasks, available.Width, dateHeight));
                return available;
            }
        }

        private void DrawDay()
        {
            var now = store.Now;
            DrawTaskList(store.Tasks.Where(t => t.IsLongTerm).ToList(), true);
            AddSectionDivider();
            DrawTaskList(store.Tasks.Where(t => store.IsOnDay(t, selected, now)).OrderByDescending(store.IsAlert).ToList(), false);
        }

        private void AddSectionDivider()
        {
            double gap = store.LongTermListCollapsed ? 2 : 12;
            nextSections.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(0, gap, 0, gap) });
        }

        private bool SetListCollapsed(bool longTerm, bool folded)
        {
            bool previous = longTerm ? store.LongTermListCollapsed : store.DayListCollapsed;
            if (previous == folded) return true;
            if (longTerm) store.LongTermListCollapsed = folded;
            else store.DayListCollapsed = folded;
            return Persist(this, delegate {
                if (longTerm) store.LongTermListCollapsed = previous;
                else store.DayListCollapsed = previous;
            });
        }

        private void DrawTaskList(List<TaskItem> tasks, bool longTerm)
        {
            var heading = new DockPanel();
            Func<bool> folded = () => longTerm ? store.LongTermListCollapsed : store.DayListCollapsed;
            var add = Button(longTerm ? "＋ 新建长期任务" : "＋ 新建任务", delegate {
                if (folded())
                {
                    if (!FinishOpenEditors()) return;
                    if (!SetListCollapsed(longTerm, false)) { Render(false); return; }
                }
                BeginDraft(longTerm);
            }, longTerm ? Sage : Leaf, longTerm ? Leaf : Brushes.White);
            add.Height = 33;
            DockPanel.SetDock(add, Dock.Right);
            heading.Children.Add(add);
            string title = longTerm ? "长期任务" : "当日任务";
            var caption = DisclosureHeading(title, HeadingSize, folded, title, delegate {
                bool hadFocus = (longTerm ? longTermHeading : dayHeading).IsKeyboardFocusWithin;
                bool ownsEditor = (draftRow != null && draftLongTerm == longTerm)
                    || (stepParent != null && stepParent.IsLongTerm == longTerm);
                if (!folded() && ownsEditor && !FinishOpenEditors()) return;
                SetListCollapsed(longTerm, !folded());
                Render(false);
                if (hadFocus) (longTerm ? longTermHeading : dayHeading).MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            });
            if (longTerm) longTermHeading = caption;
            else dayHeading = caption;
            heading.Children.Add(caption);
            nextSections.Add(heading);
            if (folded()) return;
            var summary = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 6) };
            if (!longTerm) summary.Children.Add(Label(DateLabel(selected), MetaSize, Muted));
            if (tasks.Count > 0)
            {
                var count = Label("已完成 " + tasks.Count(t => t.Done) + " / " + tasks.Count, MetaSize, Muted);
                if (!longTerm) count.Margin = new Thickness(10, 0, 0, 0);
                summary.Children.Add(count);
            }
            if (summary.Children.Count > 0) nextSections.Add(summary);
            foreach (var task in tasks) TaskRow(task, selected);
            if (draftRow != null && draftLongTerm == longTerm)
            {
                if (!longTerm && draftDay != selected) nextSections.Add(Label("新任务日期为 " + DateLabel(draftDay), MetaSize, Muted));
                nextSections.Add(draftRow);
            }
            else if (tasks.Count == 0)
            {
                var empty = Label(longTerm ? "还没有长期任务" : "当天还没有任务", ControlSize, Muted);
                empty.Margin = new Thickness(0, 5, 0, 5);
                nextSections.Add(empty);
            }
        }

        private void DrawWeek()
        {
            var start = Monday(selected);
            var end = start.AddDays(6);
            var year = Label(start.ToString("yyyy年", Zh) + (start.Year != end.Year ? " — " + end.Year + "年" : ""), MetaSize, Muted);
            nextSections.Add(year);
            nextSections.Add(Navigation(start.ToString("M月d日", Zh) + " — " + end.ToString("M月d日", Zh), true));
            var now = store.Now;
            var tasks = store.Tasks.Select(t => new { Task = t, Day = store.WeekDisplayDay(t, start, now) })
                .Where(entry => entry.Day.HasValue)
                .OrderByDescending(entry => store.IsAlert(entry.Task)).ThenBy(entry => entry.Day.Value).ToList();
            int done = tasks.Count(entry => entry.Task.Done);
            var count = Label("已完成 " + done + " / " + tasks.Count + " 项", BodySize, Leaf);
            nextSections.Add(count);
            nextSections.Add(new ProgressBar { Template = ProgressSkin, Minimum = 0, Maximum = Math.Max(1, tasks.Count), Value = done,
                Height = 5, Foreground = Leaf, Background = Sage, BorderThickness = new Thickness(0), Margin = new Thickness(0, 9, 0, 18) });
            string note = store.WeekNote(selected) ?? "";
            var panel = new StackPanel();
            var row = new DockPanel();
            var edit = Button(note.Length > 0 ? "编辑周计划" : "写周计划", delegate { EditWeek(); }, Paper, Leaf);
            DockPanel.SetDock(edit, Dock.Right);
            row.Children.Add(edit);
            row.Children.Add(Label("本周计划", HeadingSize, Ink, true));
            panel.Children.Add(row);
            var noteText = Label(note.Length > 0 ? note : "记下这周想做的事。", BodySize, note.Length > 0 ? Ink : Muted);
            noteText.TextWrapping = TextWrapping.Wrap;
            noteText.Margin = new Thickness(0, 6, 0, 0);
            panel.Children.Add(noteText);
            nextSections.Add(new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 9, 12, 12),
                Background = Header, Child = panel });
            var heading = Label("本周任务", HeadingSize, Ink, true);
            heading.Margin = new Thickness(0, 20, 0, 4);
            nextSections.Add(heading);
            if (tasks.Count == 0) Empty("本周还没有任务");
            DateTime? currentHeading = null;
            foreach (var entry in tasks)
            {
                var day = entry.Day.Value;
                if (currentHeading != day)
                {
                    var dateLabel = Label(DateLabel(day), MetaSize, Muted);
                    dateLabel.Margin = new Thickness(0, 10, 0, 3);
                    nextSections.Add(dateLabel);
                    currentHeading = day;
                }
                TaskRow(entry.Task, day);
            }
        }

        private void Empty(string info)
        {
            var text = Label(info, ControlSize, Muted);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            nextSections.Add(new Border { Background = Header, CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 17, 12, 17), Child = text, Margin = new Thickness(0, 12, 0, 0) });
        }

        private void TaskRow(TaskItem task, DateTime displayDay)
        {
            var lines = new StackPanel();
            var row = new DockPanel();
            var mainCheck = TaskCheck(task, null, displayDay);
            DockPanel.SetDock(mainCheck, Dock.Left);
            row.Children.Add(mainCheck);
            var edit = TaskAction("", delegate { EditTask(task); }, Ink);
            edit.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            edit.Padding = new Thickness(1, 0, 1, 0);
            var title = PreviewLine(task.Title, task.Done, BodySize);
            edit.Content = title;
            TaskDetails(edit, title);
            AutomationProperties.SetName(edit, "编辑任务：" + task.Title);
            row.Children.Add(new Border { Style = TaskSurface, Child = edit });
            lines.Children.Add(row);
            if (!task.IsLongTerm && task.Day != DayKey(displayDay))
                lines.Children.Add(Label("已顺延至 " + DateTime.ParseExact(task.Day, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture).ToString("M月d日", Zh), MetaSize, Muted));
            foreach (var step in task.Steps)
            {
                var line = new DockPanel();
                var check = TaskCheck(task, step, displayDay);
                DockPanel.SetDock(check, Dock.Left);
                line.Children.Add(check);
                var text = PreviewLine(step.Title, step.Done, ControlSize);
                var textArea = new Border { Background = Brushes.Transparent, Child = text };
                TaskDetails(textArea, text);
                line.Children.Add(textArea);
                lines.Children.Add(new Border { Style = TaskSurface, Child = line, Margin = new Thickness(19, 0, 0, 0) });
            }
            Button more = null;
            more = TaskAction("⋯", delegate { OpenTaskMenu(more, task); }, Muted);
            more.Width = 30;
            more.FontSize = 16;
            more.Padding = new Thickness(0);
            AutomationProperties.SetName(more, "任务操作：" + task.Title);
            var actions = new Border { Style = TaskSurface, Child = more, VerticalAlignment = VerticalAlignment.Center };
            var card = new DockPanel();
            DockPanel.SetDock(actions, Dock.Right);
            card.Children.Add(actions);
            card.Children.Add(lines);
            var group = new Border { BorderBrush = store.IsAlert(task) ? Alert : Line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 2, 0, 3),
                Background = Paper, Padding = new Thickness(4, 3, 4, 3), Child = card };
            AutomationProperties.SetName(group, "任务详情：" + task.Title);
            AutomationProperties.SetHelpText(group, String.Join(Environment.NewLine, new[] { task.Title }
                .Concat(task.Steps.Select(step => (step.Done ? "☑ " : "☐ ") + step.Title))));
            nextSections.Add(group);
            if (stepParent == task) nextSections.Add(stepRow);
        }

        private void OpenTaskMenu(Button trigger, TaskItem task)
        {
            var menu = new ContextMenu { PlacementTarget = trigger, Placement = PlacementMode.Custom };
            menu.CustomPopupPlacementCallback = delegate(Size size, Size target, Point offset)
            {
                // Align the menu's right edge with the dots so it opens over the planner first.
                var below = new Point(target.Width - size.Width, target.Height);
                var above = new Point(below.X, -size.Height);
                var pet = Owner as DesktopPet;
                if (pet != null && pet.IsVisible && PresentationSource.FromVisual(trigger) != null)
                {
                    var origin = trigger.PointToScreen(new Point());
                    var work = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
                    var screen = new Rect(work.X, work.Y, work.Width, work.Height);
                    var avoid = new Rect(pet.PointToScreen(new Point()),
                        pet.PointToScreen(new Point(pet.ActualWidth, pet.ActualHeight)));
                    foreach (var point in new[] { below, above })
                    {
                        var box = new Rect(origin + (Vector)point, size);
                        if (screen.Contains(box) && !box.IntersectsWith(avoid))
                            return new[] { new CustomPopupPlacement(point, PopupPrimaryAxis.None) };
                    }
                    var side = PetLayout.Beside(screen, avoid, size, size, 4);
                    if (!side.IsEmpty)
                        return new[] { new CustomPopupPlacement((Point)(side.Location - origin), PopupPrimaryAxis.None) };
                }
                return new[] { new CustomPopupPlacement(below, PopupPrimaryAxis.Horizontal),
                    new CustomPopupPlacement(above, PopupPrimaryAxis.Horizontal) };
            };
            bool commandChosen = false;
            Func<string, Action, MenuItem> addCommand = delegate(string label, Action action)
            {
                var item = new MenuItem { Header = label };
                item.Click += delegate { commandChosen = true; menu.IsOpen = false; action(); };
                menu.Items.Add(item);
                return item;
            };
            addCommand("添加子任务", delegate { BeginStep(task); });
            addCommand("编辑", delegate { EditTask(task); });
            addCommand("删除", delegate { if (DeleteTask(this, task)) Render(false); });
            addCommand("转长期", delegate
            {
                if (stepParent == task && !FinishStep(true)) return;
                task.IsLongTerm = true;
                Persist(this, delegate { task.IsLongTerm = false; });
                Render(false);
            }).IsEnabled = !task.IsLongTerm;
            PetMenu.ApplySurface(menu, Owner ?? this);
            menu.Closed += delegate
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate
                {
                    if (!commandChosen && IsActive && trigger.IsVisible) trigger.Focus();
                }));
            };
            trigger.ContextMenu = menu;
            trigger.Focus();
            menu.IsOpen = true;
        }

        private bool ShowPlannerPrompt(Window owner, string caption, string message, MessageBoxButton buttons)
        {
            bool ask = buttons == MessageBoxButton.YesNo;
            var dialog = new Window { Title = caption, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = ask ? 224 : 244, Height = ask ? 122 : 148, WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, FontFamily = FontFamily };
            var panel = DialogPanel(dialog, "关闭提示窗口", true);
            var ink = Paint("#493526");
            var edge = Paint("#BD946D");
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12, 0, 12, 9) };
            if (ask)
            {
                var yes = Button("是(Y)", delegate { dialog.DialogResult = true; }, Paint("#FFF6E9"), ink);
                yes.MinWidth = 60;
                yes.FocusVisualStyle = null;
                yes.BorderBrush = edge;
                yes.Margin = new Thickness(0, 0, 6, 0);
                AutomationProperties.SetAutomationId(yes, "ConfirmYes");
                actions.Children.Add(yes);
                dialog.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
                {
                    var key = e.Key == Key.System ? e.SystemKey : e.Key;
                    if (Keyboard.Modifiers != ModifierKeys.None && Keyboard.Modifiers != ModifierKeys.Alt) return;
                    if (key != Key.Y && key != Key.N) return;
                    e.Handled = true;
                    dialog.DialogResult = key == Key.Y;
                };
            }
            var dismiss = Button(ask ? "否(N)" : "确定", delegate { dialog.DialogResult = false; }, Paint("#E8CAA8"), ink);
            dismiss.MinWidth = 60;
            dismiss.BorderBrush = edge;
            dismiss.FocusVisualStyle = null;
            dismiss.IsCancel = true;
            dismiss.IsDefault = true;
            AutomationProperties.SetAutomationId(dismiss, ask ? "ConfirmNo" : "ConfirmOK");
            actions.Children.Add(dismiss);
            DockPanel.SetDock(actions, Dock.Bottom);
            panel.Children.Add(actions);
            var prompt = Label(message, BodySize, ink);
            prompt.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(new ScrollViewer { Content = prompt, Margin = new Thickness(12, 4, 12, 5),
                VerticalContentAlignment = ask ? VerticalAlignment.Center : VerticalAlignment.Top,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly });
            dialog.ContentRendered += delegate { dismiss.Focus(); };
            var pet = Owner as DesktopPet;
            if (pet != null) pet.WatchPlannerWindow(dialog);
            return dialog.ShowDialog() == true;
        }

        private bool DeleteTask(Window owner, TaskItem task)
        {
            if (!ShowPlannerPrompt(owner, "删除任务", "删除这项任务？", MessageBoxButton.YesNo)) return false;
            int index = store.Tasks.IndexOf(task);
            store.Tasks.RemoveAt(index);
            if (!Persist(owner, delegate { store.Tasks.Insert(index, task); })) return false;
            if (stepParent == task) ClearStep();
            return true;
        }

        private static Button TaskAction(string text, Action act, Brush color)
        {
            var button = Button(text, act, Brushes.Transparent, color);
            button.BorderThickness = new Thickness(0);
            button.FocusVisualStyle = null;
            return button;
        }

        private static TextBlock PreviewLine(string title, bool done, double size)
        {
            var label = Label(title, size, done ? Muted : Ink);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            if (done) label.TextDecorations = TextDecorations.Strikethrough;
            return label;
        }

        private CheckBox TaskCheck(TaskItem task, PlanStep step, DateTime displayDay)
        {
            var check = new CheckBox { IsChecked = step == null ? task.Done : step.Done, Template = CheckSkin, FocusVisualStyle = null,
                Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            AutomationProperties.SetName(check, step == null ? "完成任务：" + task.Title : "完成子任务：" + step.Title);
            check.Click += delegate
            {
                bool wasDone = task.Done;
                var before = task.Steps.Select(item => item.Done).ToArray();
                bool mark = check.IsChecked == true;
                if (step == null)
                {
                    foreach (var item in task.Steps) item.Done = mark;
                    task.Done = mark;
                }
                else
                {
                    step.Done = mark;
                    task.Done = task.Steps.All(item => item.Done);
                }
                if (!Persist(this, delegate
                {
                    task.Done = wasDone;
                    for (int i = 0; i < before.Length; i++) task.Steps[i].Done = before[i];
                    check.IsChecked = step == null ? task.Done : step.Done;
                })) return;
                Render(false);
                var completed = TaskCompleted;
                if (task.Done && !wasDone && completed != null)
                {
                    var now = store.Now;
                    completed(!task.IsLongTerm && store.Tasks.All(item => item.Done || !store.IsOnDay(item, displayDay, now)));
                }
            };
            return check;
        }

        private static bool IsClipped(TextBlock label)
        {
            var measure = new FormattedText(label.Text, Zh, label.FlowDirection,
                new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch),
                label.FontSize, label.Foreground, null, TextOptions.GetTextFormattingMode(label),
                VisualTreeHelper.GetDpi(label).PixelsPerDip);
            return label.ActualWidth > 0 && measure.Width > label.ActualWidth + 0.01;
        }

        private void TaskDetails(FrameworkElement textArea, TextBlock label)
        {
            var details = Label(label.Text, BodySize, Ink);
            details.TextWrapping = TextWrapping.Wrap;
            details.MaxWidth = 330;
            var tip = new ToolTip { Content = details, PlacementTarget = textArea, Template = PetMenu.TaskHintTemplate,
                FontFamily = FontFamily };
            textArea.ToolTip = tip;
            ToolTipService.SetInitialShowDelay(textArea, 700);
            ToolTipService.SetBetweenShowDelay(textArea, 0);
            ToolTipService.SetShowDuration(textArea, 60000);
            textArea.ToolTipOpening += delegate(object sender, ToolTipEventArgs e) { if (!IsClipped(label)) e.Handled = true; };
            label.SizeChanged += delegate { if (tip.IsOpen && !IsClipped(label)) tip.IsOpen = false; };
            textArea.Unloaded += delegate { if (tip.IsOpen) tip.IsOpen = false; };
            textArea.IsVisibleChanged += delegate { if (!textArea.IsVisible && tip.IsOpen) tip.IsOpen = false; };
            // The owner is set after Planner's constructor; query it when the tooltip actually opens.
            tip.Placement = PlacementMode.Custom;
            ToolTipService.SetPlacement(textArea, PlacementMode.Custom);
            tip.CustomPopupPlacementCallback = delegate(Size size, Size target, Point offset)
            {
                // WPF can request one last layout after a task card has been detached.
                var source = PresentationSource.FromVisual(textArea) as HwndSource;
                if (!textArea.IsVisible || source == null || source.IsDisposed || source != PresentationSource.FromVisual(this))
                    return new[] { new CustomPopupPlacement(new Point(0, target.Height), PopupPrimaryAxis.Horizontal) };
                Point origin = textArea.PointToScreen(new Point());
                var pointer = Forms.Cursor.Position;
                var area = Forms.Screen.FromHandle(source.Handle).WorkingArea;
                var screen = new Rect(area.X, area.Y, area.Width, area.Height);
                double x = Math.Max(screen.Left, Math.Min(pointer.X, screen.Right - size.Width));
                double y = pointer.Y + 18 + size.Height <= screen.Bottom ? pointer.Y + 18 : pointer.Y - size.Height - 7;
                var candidate = new Rect(new Point(x, Math.Max(screen.Top, y)), size);
                var pet = Owner as DesktopPet;
                if (pet != null && pet.IsVisible && PresentationSource.FromVisual(pet) != null)
                {
                    var avoid = new Rect(pet.PointToScreen(new Point()),
                        pet.PointToScreen(new Point(pet.ActualWidth, pet.ActualHeight)));
                    if (candidate.IntersectsWith(avoid))
                    {
                        var side = PetLayout.Beside(screen, avoid, size, new Size(), 6);
                        if (!side.IsEmpty) candidate.Location = side.Location;
                    }
                }
                return new[] { new CustomPopupPlacement(new Point(candidate.X - origin.X, candidate.Y - origin.Y), PopupPrimaryAxis.None) };
            };
        }

        private bool Persist(Window owner, Action rollback = null)
        {
            try { store.Save(); return true; }
            catch (Exception error)
            {
                if (rollback != null) rollback();
                ShowPlannerPrompt(owner, "小黑手帐", "保存失败，请重试。\n" + error.Message, MessageBoxButton.OK);
                return false;
            }
        }

        private void EditTask(TaskItem task)
        {
            if (stepRow != null)
            {
                if (!FinishStep(true)) return;
                Render(false);
            }
            bool wasLongTerm = task.IsLongTerm;
            string context = wasLongTerm ? "长期任务" :
                DateLabel(DateTime.ParseExact(task.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture));
            string origTitle = task.Title;
            string origNote = task.Notes ?? "";
            var originalSteps = task.Steps.ToList();
            bool originalDone = task.Done;
            Editor("编辑任务", context, origTitle, origNote,
                delegate(Window dialog, string name, string body, bool convertToLongTerm)
                {
                    task.Title = name;
                    if (body != origNote)
                    {
                        task.SetPlan(body);
                        if (task.Steps.Count > 0) task.Done = task.Steps.All(step => step.Done);
                    }
                    if (convertToLongTerm) task.IsLongTerm = true;
                    return Persist(dialog, delegate
                    {
                        task.IsLongTerm = wasLongTerm;
                        task.Title = origTitle;
                        task.Steps.Clear();
                        task.Steps.AddRange(originalSteps);
                        task.Done = originalDone;
                    });
                }, task);
        }

        private void EditWeek()
        {
            var monday = Monday(selected);
            string note = store.WeekNote(monday) ?? "";
            Editor("编辑周计划", monday.ToString("M月d日", Zh) + " — " + monday.AddDays(6).ToString("M月d日", Zh), null, note,
                delegate(Window dialog, string unused, string body, bool unusedConversion)
                {
                    store.SetWeekNote(monday, body);
                    if (Persist(dialog)) return true;
                    store.SetWeekNote(monday, note);
                    return false;
                }, null);
        }

        private static DockPanel DialogPanel(Window dialog, string closeLabel, bool prompt = false)
        {
            var basePanel = new DockPanel();
            var sheet = RoundedSheet(basePanel);
            sheet.Background = Paint(prompt ? "#F8EAD7" : "#FBF5EB");
            sheet.BorderBrush = Paint(prompt ? "#B68A62" : "#CEBBA2");
            dialog.Content = sheet;
            var titleInk = Paint("#493526");
            var titleFill = Paint(prompt ? "#EFD6B7" : "#F2E5D2");
            var titleBar = new DockPanel { Background = titleFill };
            var titleBand = new Border { Child = titleBar, Background = titleFill,
                Padding = prompt ? new Thickness(12, 2, 6, 2) : new Thickness(14, 3, 8, 3) };
            DockPanel.SetDock(titleBand, Dock.Top);
            basePanel.Children.Add(titleBand);
            titleBar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            { if (e.ButtonState == MouseButtonState.Pressed) dialog.DragMove(); };
            var close = Button("×", delegate { dialog.Close(); }, Brushes.Transparent, titleInk);
            close.FontSize = 20;
            close.Width = 28;
            close.Padding = new Thickness(0);
            Accessible(close, closeLabel);
            DockPanel.SetDock(close, Dock.Right);
            titleBar.Children.Add(close);
            var title = Label(dialog.Title, HeadingSize, titleInk, true);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.ToolTip = dialog.Title;
            titleBar.Children.Add(title);
            return basePanel;
        }

        private void Editor(string caption, string date, string name, string originalBody,
            Func<Window, string, string, bool, bool> save, TaskItem existing)
        {
            bool taskMode = name != null;
            var editor = new Window { Title = caption, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = Math.Min(332, ActualWidth), Height = Math.Min(taskMode ? 312 : 260, ActualHeight),
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, FontFamily = FontFamily };
            var basePanel = DialogPanel(editor, "关闭编辑窗口");
            var shell = new DockPanel { Margin = new Thickness(15, 10, 15, 12) };
            basePanel.Children.Add(shell);
            var bottom = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(bottom, Dock.Bottom);
            shell.Children.Add(bottom);
            var fields = new StackPanel();
            shell.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                PanningMode = PanningMode.VerticalOnly, Content = fields });
            var contextRow = new DockPanel();
            CheckBox conversion = null;
            if (existing != null && !existing.IsLongTerm)
            {
                conversion = new CheckBox { Content = "转为长期任务", Template = CheckSkin, FocusVisualStyle = null, Foreground = Leaf, FontSize = ControlSize,
                    VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
                AutomationProperties.SetName(conversion, "转为长期任务");
                DockPanel.SetDock(conversion, Dock.Right);
                contextRow.Children.Add(conversion);
            }
            contextRow.Children.Add(Label(date, BodySize, Leaf));
            fields.Children.Add(contextRow);
            var titleBox = new TextBox { Template = InputSkin, Text = name ?? "", Background = Paper,
                FontSize = BodySize, Padding = new Thickness(6), BorderBrush = TodayRing };
            if (taskMode)
            {
                Field(fields, "任务名称", titleBox);
            }
            var bodyBox = new TextBox { Template = InputSkin, Text = originalBody, Background = taskMode ? Paper : Header,
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = BodySize, Height = taskMode ? 92 : 106,
                Padding = new Thickness(taskMode ? 5 : 8), BorderBrush = taskMode ? TodayRing : Line,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, FocusVisualStyle = null };
            Field(fields, taskMode ? "子任务（每行一项）" : "本周计划", bodyBox);
            var hint = Label("", MetaSize, Paint("#9B4036"));
            fields.Children.Add(hint);
            bool accepted = false;
            Action commit = delegate
            {
                if (taskMode && String.IsNullOrWhiteSpace(titleBox.Text))
                {
                    hint.Text = "先写下任务名称。";
                    titleBox.Focus();
                    return;
                }
                string body = bodyBox.Text == originalBody ? originalBody : bodyBox.Text.Trim();
                if (!save(editor, titleBox.Text.Trim(), body, conversion != null && conversion.IsChecked == true)) return;
                accepted = true;
                editor.Close();
            };
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var cancel = Button("取消", delegate { editor.Close(); }, Sage, Leaf);
            cancel.MinWidth = 68;
            cancel.FocusVisualStyle = null;
            actions.Children.Add(cancel);
            var submit = Button("保存", commit, Leaf, Brushes.White);
            submit.FocusVisualStyle = null;
            submit.Margin = new Thickness(6, 0, 0, 0);
            submit.MinWidth = 68;
            actions.Children.Add(submit);
            DockPanel.SetDock(actions, Dock.Right);
            bottom.Children.Add(actions);
            if (existing != null)
            {
                var delete = Button("删除任务", delegate
                {
                    if (DeleteTask(editor, existing)) { accepted = true; editor.Close(); }
                }, Brushes.Transparent, Muted);
                delete.HorizontalAlignment = HorizontalAlignment.Left;
                bottom.Children.Add(delete);
            }
            editor.ContentRendered += delegate
            {
                if (taskMode) titleBox.Focus(); else bodyBox.Focus();
            };
            editor.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { e.Handled = true; editor.Close(); }
                else if (e.Key == Key.Enter && (Keyboard.Modifiers == ModifierKeys.Control || titleBox.IsKeyboardFocused))
                { e.Handled = true; commit(); }
            };
            editor.Closing += delegate(object sender, CancelEventArgs e)
            {
                bool converted = conversion != null && conversion.IsChecked == true;
                if (!accepted && (titleBox.Text != (name ?? "") || bodyBox.Text != originalBody || converted))
                    e.Cancel = !ShowPlannerPrompt(editor, caption, "放弃未保存的修改？", MessageBoxButton.YesNo);
            };
            var pet = Owner as DesktopPet;
            if (pet != null) pet.WatchPlannerWindow(editor);
            editor.ShowDialog();
            if (accepted) Render(false);
        }

        private static void Field(StackPanel fields, string title, TextBox box)
        {
            var label = Label(title, ControlSize, Muted);
            label.Margin = new Thickness(0, 10, 0, 4);
            fields.Children.Add(label);
            AutomationProperties.SetName(box, title);
            fields.Children.Add(box);
        }
    }
}
