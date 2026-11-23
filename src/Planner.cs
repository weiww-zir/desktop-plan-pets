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
            "<Grid Width='27' Height='28' Background='Transparent'><Border x:Name='halo' Width='25' Height='25' CornerRadius='9' Background='Transparent'/>" +
            "<Border x:Name='box' Width='18' Height='18' CornerRadius='6' Background='#FCFDF9' BorderBrush='#72907D' BorderThickness='1.5'/>" +
            "<Path x:Name='tick' Data='M 1,5 L 4,8 L 10,1' Stroke='White' StrokeThickness='2' StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round' HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Hidden'/></Grid>" +
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
        private DateTime selected;
        private bool weekly;

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
            close.FontSize = 22;
            close.Width = 32;
            close.Height = 32;
            close.Padding = new Thickness(0);
            Accessible(close, "收起手帐");
            DockPanel.SetDock(close, Dock.Right);
            masthead.Children.Add(close);
            var mark = new Viewbox { Width = 32, Height = 31, Child = PetArt.Create(false), Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(mark, Dock.Left);
            masthead.Children.Add(mark);
            masthead.Children.Add(Label("小黑手帐", 18, Ink, true));

            var tabs = new Border { Padding = new Thickness(12, 3, 12, 0), BorderBrush = Line,
                BorderThickness = new Thickness(0, 0, 0, 1), Child = modes };
            DockPanel.SetDock(tabs, Dock.Top);
            frame.Children.Add(tabs);
            var footer = new Border { Height = 22 };
            DockPanel.SetDock(footer, Dock.Bottom);
            frame.Children.Add(footer);
            InstallFrameGestures(top, close, AddResizeHandles(surface));
            sections.Margin = new Thickness(14, 8, 14, 4);
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.PanningMode = PanningMode.VerticalOnly;
            scroll.Content = sections;
            frame.Children.Add(scroll);
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
            var button = new Button { Content = text, Template = ButtonSkin, FontSize = 13, MinHeight = 28,
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
                nextSections.Add(Label("为「" + stepParent.Title + "」添加子任务", 13, Muted));
                nextSections.Add(stepRow);
            }
            for (int i = sections.Children.Count - 1; i >= 0; i--)
                if (sections.Children[i] != draftRow && sections.Children[i] != stepRow)
                    sections.Children.RemoveAt(i);
            for (int i = 0; i < nextSections.Count; i++)
                if (nextSections[i] != draftRow && nextSections[i] != stepRow || !sections.Children.Contains(nextSections[i]))
                    sections.Children.Insert(i, nextSections[i]);
            if (reset) scroll.ScrollToTop();
        }

        private void AddTab(string text, bool week)
        {
            bool active = weekly == week;
            var tab = Button(text, delegate { if (weekly != week && FinishOpenEditors()) { weekly = week; Render(true); } },
                Brushes.Transparent, active ? Leaf : Muted);
            tab.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
            tab.Height = 30;
            modes.Children.Add(new Border { Child = tab, BorderBrush = active ? Leaf : Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 2), Margin = new Thickness(4, 0, 4, 0) });
        }

        private void Go(DateTime date)
        {
            if (date != selected && !FinishOpenEditors()) return;
            selected = date;
            Render(true);
        }

        private void Navigation(string title, bool week)
        {
            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 9) };
            var nav = new StackPanel { Orientation = Orientation.Horizontal };
            var prev = Button("‹", delegate { Go(week ? selected.AddDays(-7) : selected.AddMonths(-1)); });
            var next = Button("›", delegate { Go(week ? selected.AddDays(7) : selected.AddMonths(1)); });
            prev.FontSize = next.FontSize = 20;
            prev.Width = next.Width = 28;
            prev.Padding = next.Padding = new Thickness(0);
            Accessible(prev, week ? "上一周" : "上个月");
            Accessible(next, week ? "下一周" : "下个月");
            nav.Children.Add(prev);
            nav.Children.Add(Button(week ? "本周" : "今天", delegate { Go(store.Now.Date); }, Sage, Leaf));
            nav.Children.Add(next);
            DockPanel.SetDock(nav, Dock.Right);
            line.Children.Add(nav);
            line.Children.Add(Label(title, week ? 15 : 19, Ink, true));
            nextSections.Add(line);
        }

        private void DrawDay()
        {
            Navigation(selected.ToString("yyyy年 M月", Zh), false);
            var grid = new UniformGrid { Columns = 7 };
            foreach (var name in Days)
            {
                var label = Label(name, 12, Muted);
                label.TextAlignment = TextAlignment.Center;
                label.Height = 22;
                grid.Children.Add(label);
            }
            var month = new DateTime(selected.Year, selected.Month, 1);
            var first = Monday(month);
            int cells = (int)Math.Ceiling(((month - first).Days + DateTime.DaysInMonth(month.Year, month.Month)) / 7.0) * 7;
            var now = store.Now;
            for (int i = 0; i < cells; i++)
            {
                var date = first.AddDays(i);
                bool chosen = date == selected, today = date == now.Date;
                bool hasTasks = store.Tasks.Any(t => store.IsOnDay(t, date, now));
                var button = Button("", delegate { Go(date); }, chosen ? Leaf : Brushes.Transparent);
                button.Margin = new Thickness(2, 1, 2, 1);
                button.MinHeight = 24;
                button.Height = 26;
                button.Padding = new Thickness(0);
                if (today && !chosen) button.BorderBrush = TodayRing;
                var color = chosen ? Brushes.White : date.Month == selected.Month ? Ink : Muted;
                var content = new Grid();
                var number = Label(date.Day.ToString(), 13, color, chosen || today);
                number.TextAlignment = TextAlignment.Center;
                content.Children.Add(number);
                if (hasTasks)
                    content.Children.Add(new Border { Width = 8, Height = 2, CornerRadius = new CornerRadius(1),
                        Background = chosen ? Brushes.White : Leaf, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -3) });
                button.Content = content;
                Accessible(button, DateLabel(date) + (hasTasks ? "，有任务" : ""));
                grid.Children.Add(button);
            }
            nextSections.Add(grid);
            Divider();
            var tasks = store.Tasks.Where(t => store.IsOnDay(t, selected, now))
                .OrderByDescending(store.IsAlert).ToList();
            var dayRow = new DockPanel();
            var add = Button("＋ 新建任务", BeginDraft, Leaf, Brushes.White);
            add.Height = 33;
            DockPanel.SetDock(add, Dock.Right);
            dayRow.Children.Add(add);
            dayRow.Children.Add(Label(DateLabel(selected), 17, Ink, true));
            nextSections.Add(dayRow);
            if (tasks.Count == 0 && draftRow == null) Empty("当天还没有任务");
            if (tasks.Count > 0)
            {
                var count = Label("已完成 " + tasks.Count(t => t.Done) + " / " + tasks.Count, 12, Muted);
                count.Margin = new Thickness(0, 2, 0, 7);
                nextSections.Add(count);
                foreach (var task in tasks) TaskRow(task, selected);
            }
            if (draftRow != null)
            {
                if (draftDay != selected) nextSections.Add(Label("新任务日期为 " + DateLabel(draftDay), 12, Muted));
                nextSections.Add(draftRow);
            }
        }

        private void DrawWeek()
        {
            var start = Monday(selected);
            var end = start.AddDays(6);
            var year = Label(start.ToString("yyyy年", Zh) + (start.Year != end.Year ? " — " + end.Year + "年" : ""), 12, Muted);
            nextSections.Add(year);
            Navigation(start.ToString("M月d日", Zh) + " — " + end.ToString("M月d日", Zh), true);
            var now = store.Now;
            var tasks = store.Tasks.Select(t => new { Task = t, Day = store.WeekDisplayDay(t, start, now) })
                .Where(entry => entry.Day.HasValue)
                .OrderByDescending(entry => store.IsAlert(entry.Task)).ThenBy(entry => entry.Day.Value).ToList();
            int done = tasks.Count(entry => entry.Task.Done);
            var count = Label("已完成 " + done + " / " + tasks.Count + " 项", 15, Leaf, true);
            nextSections.Add(count);
            nextSections.Add(new ProgressBar { Template = ProgressSkin, Minimum = 0, Maximum = Math.Max(1, tasks.Count), Value = done,
                Height = 5, Foreground = Leaf, Background = Sage, BorderThickness = new Thickness(0), Margin = new Thickness(0, 9, 0, 18) });
            string note = store.WeekNote(selected) ?? "";
            var panel = new StackPanel();
            var row = new DockPanel();
            var edit = Button(note.Length > 0 ? "编辑周计划" : "写周计划", delegate { EditWeek(); }, Paper, Leaf);
            DockPanel.SetDock(edit, Dock.Right);
            row.Children.Add(edit);
            row.Children.Add(Label("本周计划", 15, Ink, true));
            panel.Children.Add(row);
            var noteText = Label(note.Length > 0 ? note : "记下这周想做的事。", 13, note.Length > 0 ? Ink : Muted);
            noteText.TextWrapping = TextWrapping.Wrap;
            noteText.Margin = new Thickness(0, 6, 0, 0);
            panel.Children.Add(noteText);
            nextSections.Add(new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 9, 12, 12),
                Background = Header, Child = panel });
            var heading = Label("本周任务", 17, Ink, true);
            heading.Margin = new Thickness(0, 20, 0, 4);
            nextSections.Add(heading);
            if (tasks.Count == 0) Empty("本周还没有任务");
            DateTime? currentHeading = null;
            foreach (var entry in tasks)
            {
                var day = entry.Day.Value;
                if (currentHeading != day)
                {
                    var dateLabel = Label(DateLabel(day), 12, Muted);
                    dateLabel.Margin = new Thickness(0, 10, 0, 3);
                    nextSections.Add(dateLabel);
                    currentHeading = day;
                }
                TaskRow(entry.Task, day);
            }
        }

        private void Divider()
        {
            nextSections.Add(new Border { Background = Line, Height = 1, Margin = new Thickness(0, 14, 0, 14) });
        }

        private void Empty(string info)
        {
            var text = Label(info, 14, Muted);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            nextSections.Add(new Border { Background = Header, CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 17, 12, 17), Child = text, Margin = new Thickness(0, 12, 0, 0) });
        }

        private void TaskRow(TaskItem task, DateTime displayDay)
        {
            var lines = new StackPanel();
            var labels = new List<TextBlock>();
            var row = new DockPanel();
            var mainCheck = TaskCheck(task, null, displayDay);
            DockPanel.SetDock(mainCheck, Dock.Left);
            row.Children.Add(mainCheck);
            var add = TaskAction("＋ 子任务", delegate { BeginStep(task); }, Leaf);
            add.Padding = new Thickness(4, 0, 4, 0);
            add.FontSize = 12;
            AutomationProperties.SetName(add, "添加子任务：" + task.Title);
            var addSurface = new Border { Style = TaskSurface, Child = add };
            DockPanel.SetDock(addSurface, Dock.Right);
            row.Children.Add(addSurface);
            var edit = TaskAction("", delegate { EditTask(task); }, Ink);
            edit.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            edit.Padding = new Thickness(1, 0, 1, 0);
            var title = PreviewLine(task.Title, task.Done, 14);
            labels.Add(title);
            edit.Content = title;
            AutomationProperties.SetName(edit, "编辑任务：" + task.Title);
            row.Children.Add(new Border { Style = TaskSurface, Child = edit });
            lines.Children.Add(row);
            if (task.Day != DayKey(displayDay))
                lines.Children.Add(Label("已顺延至 " + DateTime.ParseExact(task.Day, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture).ToString("M月d日", Zh), 12, Muted));
            foreach (var step in task.Steps)
            {
                var line = new DockPanel();
                var check = TaskCheck(task, step, displayDay);
                DockPanel.SetDock(check, Dock.Left);
                line.Children.Add(check);
                var text = PreviewLine(step.Title, step.Done, 13);
                labels.Add(text);
                line.Children.Add(text);
                lines.Children.Add(new Border { Style = TaskSurface, Child = line, Margin = new Thickness(19, 0, 0, 0) });
            }
            var group = new Border { BorderBrush = store.IsAlert(task) ? Alert : Line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 2, 0, 3),
                Background = Paper, Padding = new Thickness(4, 3, 4, 3), Child = lines };
            TaskDetails(group, task, labels);
            nextSections.Add(group);
            if (stepParent == task) nextSections.Add(stepRow);
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
                if (!Persist(this))
                {
                    task.Done = wasDone;
                    for (int i = 0; i < before.Length; i++) task.Steps[i].Done = before[i];
                    check.IsChecked = step == null ? task.Done : step.Done;
                    return;
                }
                Render(false);
                var completed = TaskCompleted;
                if (task.Done && !wasDone && completed != null)
                {
                    var now = store.Now;
                    completed(store.Tasks.All(item => item.Done || !store.IsOnDay(item, displayDay, now)));
                }
            };
            return check;
        }

        private static bool IsClipped(TextBlock label)
        {
            var measure = new FormattedText(label.Text, Zh, label.FlowDirection,
                new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch),
                label.FontSize, label.Foreground, VisualTreeHelper.GetDpi(label).PixelsPerDip);
            return measure.WidthIncludingTrailingWhitespace > label.ActualWidth + 0.5;
        }

        private void TaskDetails(Border row, TaskItem task, List<TextBlock> labels)
        {
            string full = String.Join(Environment.NewLine, new[] { task.Title }
                .Concat(task.Steps.Select(step => (step.Done ? "☑ " : "☐ ") + step.Title)));
            AutomationProperties.SetName(row, "任务详情：" + task.Title);
            AutomationProperties.SetHelpText(row, full);
            var details = Label(full, 13, Ink);
            details.TextWrapping = TextWrapping.Wrap;
            details.MaxWidth = 330;
            var tip = new ToolTip { Content = details, PlacementTarget = row, Template = PetMenu.HintTemplate,
                FontFamily = FontFamily, Background = Paper, BorderBrush = TodayRing,
                Padding = new Thickness(10, 7, 10, 7) };
            row.ToolTip = tip;
            ToolTipService.SetInitialShowDelay(row, 700);
            ToolTipService.SetBetweenShowDelay(row, 0);
            ToolTipService.SetShowDuration(row, 60000);
            row.ToolTipOpening += delegate(object sender, ToolTipEventArgs e)
            {
                if (labels.Any(IsClipped)) return;
                var offset = row.TranslatePoint(new Point(), scroll);
                if (offset.Y >= 0 && offset.Y + row.ActualHeight <= scroll.ViewportHeight + 1)
                    e.Handled = true;
            };
            // The owner is set after Planner's constructor; query it when the tooltip actually opens.
            tip.Placement = PlacementMode.Custom;
            ToolTipService.SetPlacement(row, PlacementMode.Custom);
            tip.CustomPopupPlacementCallback = delegate(Size size, Size target, Point offset)
            {
                Point origin = row.PointToScreen(new Point());
                var pointer = Forms.Cursor.Position;
                var area = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
                var screen = new Rect(area.X, area.Y, area.Width, area.Height);
                double x = Math.Max(screen.Left, Math.Min(pointer.X, screen.Right - size.Width));
                double y = pointer.Y + 18 + size.Height <= screen.Bottom ? pointer.Y + 18 : pointer.Y - size.Height - 7;
                var candidate = new Rect(new Point(x, Math.Max(screen.Top, y)), size);
                var pet = Owner as DesktopPet;
                if (pet != null && pet.IsVisible)
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

        private bool Persist(Window owner)
        {
            try { store.Save(); return true; }
            catch (Exception error)
            {
                MessageBox.Show(owner, "保存失败，请重试。\n" + error.Message, "小黑手帐", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            var date = DateTime.ParseExact(task.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            string origTitle = task.Title;
            string origNote = task.Notes ?? "";
            var originalSteps = task.Steps.ToList();
            bool originalDone = task.Done;
            Editor("编辑任务", DateLabel(date), origTitle, origNote,
                delegate(Window dialog, string name, string body)
                {
                    task.Title = name;
                    task.SetPlan(body);
                    if (task.Steps.Count > 0) task.Done = task.Steps.All(step => step.Done);
                    if (Persist(dialog)) return true;
                    task.Title = origTitle;
                    task.Steps.Clear();
                    task.Steps.AddRange(originalSteps);
                    task.Done = originalDone;
                    return false;
                }, task);
        }

        private void EditWeek()
        {
            var monday = Monday(selected);
            string note = store.WeekNote(monday) ?? "";
            Editor("编辑周计划", monday.ToString("M月d日", Zh) + " — " + monday.AddDays(6).ToString("M月d日", Zh), null, note,
                delegate(Window dialog, string unused, string body)
                {
                    store.SetWeekNote(monday, body);
                    if (Persist(dialog)) return true;
                    store.SetWeekNote(monday, note);
                    return false;
                }, null);
        }

        private void Editor(string caption, string date, string name, string originalBody,
            Func<Window, string, string, bool> save, TaskItem existing)
        {
            bool taskMode = name != null;
            var editor = new Window { Title = caption, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = Math.Min(365, ActualWidth), Height = Math.Min(taskMode ? 334 : 290, ActualHeight),
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, FontFamily = FontFamily };
            var basePanel = new DockPanel();
            editor.Content = RoundedSheet(basePanel);
            var titleBar = new DockPanel { Background = Header };
            var titleBand = new Border { Child = titleBar, Background = Header, Padding = new Thickness(15, 5, 9, 5) };
            DockPanel.SetDock(titleBand, Dock.Top);
            basePanel.Children.Add(titleBand);
            titleBar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            { if (e.ButtonState == MouseButtonState.Pressed) editor.DragMove(); };
            var close = Button("×", delegate { editor.Close(); });
            close.FontSize = 20;
            close.Width = 30;
            close.Padding = new Thickness(0);
            Accessible(close, "关闭编辑窗口");
            DockPanel.SetDock(close, Dock.Right);
            titleBar.Children.Add(close);
            titleBar.Children.Add(Label(caption, 16, Ink, true));
            var shell = new DockPanel { Margin = new Thickness(15, 10, 15, 12) };
            basePanel.Children.Add(shell);
            var bottom = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(bottom, Dock.Bottom);
            shell.Children.Add(bottom);
            var fields = new StackPanel();
            shell.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                PanningMode = PanningMode.VerticalOnly, Content = fields });
            fields.Children.Add(Label(date, 15, Leaf, true));
            var titleBox = new TextBox { Template = InputSkin, Text = name ?? "", Background = Paper,
                FontSize = 15, Padding = new Thickness(6), BorderBrush = TodayRing };
            if (taskMode)
            {
                Field(fields, "任务名称", titleBox);
            }
            var bodyBox = new TextBox { Template = InputSkin, Text = originalBody, Background = Paper, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                FontSize = 14, Height = taskMode ? 92 : 106, Padding = new Thickness(5),
                BorderBrush = TodayRing, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
            Field(fields, taskMode ? "子任务（每行一项）" : "本周计划", bodyBox);
            var hint = Label("", 12, Paint("#9B4036"));
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
                if (!save(editor, titleBox.Text.Trim(), bodyBox.Text.Trim())) return;
                accepted = true;
                editor.Close();
            };
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(Button("取消", delegate { editor.Close(); }));
            var submit = Button("保存", commit, Leaf, Brushes.White);
            submit.Margin = new Thickness(6, 0, 0, 0);
            submit.MinWidth = 68;
            actions.Children.Add(submit);
            DockPanel.SetDock(actions, Dock.Right);
            bottom.Children.Add(actions);
            if (existing != null)
            {
                var delete = Button("删除任务", delegate
                {
                    if (MessageBox.Show(editor, "删除这项任务？", "删除任务", MessageBoxButton.YesNo, MessageBoxImage.Question,
                        MessageBoxResult.No) != MessageBoxResult.Yes) return;
                    int index = store.Tasks.IndexOf(existing);
                    store.Tasks.RemoveAt(index);
                    if (Persist(editor)) { accepted = true; editor.Close(); }
                    else store.Tasks.Insert(index, existing);
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
                if (!accepted && (titleBox.Text != (name ?? "") || bodyBox.Text != originalBody))
                    e.Cancel = MessageBox.Show(editor, "放弃未保存的修改？", caption, MessageBoxButton.YesNo,
                        MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes;
            };
            var pet = Owner as DesktopPet;
            if (pet != null) pet.WatchPlannerWindow(editor);
            editor.ShowDialog();
            if (accepted) Render(false);
        }

        private static void Field(StackPanel fields, string title, TextBox box)
        {
            var label = Label(title, 13, Ink, true);
            label.Margin = new Thickness(0, 10, 0, 4);
            fields.Children.Add(label);
            AutomationProperties.SetName(box, title);
            fields.Children.Add(box);
        }
    }
}
