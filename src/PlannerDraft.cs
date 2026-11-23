using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Xiaohei
{
    internal sealed partial class Planner
    {
        private Border draftRow;
        private TextBox draftTitle, draftNotes;
        private TextBlock draftHint;
        private DateTime draftDay;
        private bool draftComposing;
        private TaskItem stepParent;
        private Border stepRow;
        private TextBox stepTitle;

        private bool FinishOpenEditors()
        {
            return FinishDraft(true) && FinishStep(true);
        }

        private void BeginStep(TaskItem parent)
        {
            if (stepParent != parent)
            {
                if (!FinishOpenEditors()) return;
                stepParent = parent;
                var layout = new DockPanel();
                stepRow = new Border { Child = layout, Padding = new Thickness(4),
                    Margin = new Thickness(16, 0, 0, 5), CornerRadius = new CornerRadius(10),
                    BorderThickness = new Thickness(1), BorderBrush = TodayRing, Background = Paper };
                var cancel = Button("取消", CancelStep, Brushes.Transparent, Muted);
                AutomationProperties.SetName(cancel, "取消子任务");
                DockPanel.SetDock(cancel, Dock.Right);
                layout.Children.Add(cancel);
                var save = Button("保存", delegate { if (FinishStep(false)) Render(false); }, Leaf, Brushes.White);
                AutomationProperties.SetName(save, "保存子任务");
                DockPanel.SetDock(save, Dock.Right);
                layout.Children.Add(save);
                var input = new Grid { Margin = new Thickness(0, 0, 4, 0) };
                stepTitle = new TextBox { Template = InputSkin, Background = Paper, BorderBrush = Line,
                    Padding = new Thickness(6, 3, 6, 3), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
                AutomationProperties.SetName(stepTitle, "子任务名称");
                input.Children.Add(stepTitle);
                var hint = Label("输入子任务名称", 12, Muted);
                hint.IsHitTestVisible = false;
                hint.Margin = new Thickness(7, 0, 0, 0);
                input.Children.Add(hint);
                layout.Children.Add(input);
                save.IsEnabled = false;
                save.Opacity = 0.55;
                stepTitle.TextChanged += delegate
                {
                    save.IsEnabled = !String.IsNullOrWhiteSpace(stepTitle.Text);
                    save.Opacity = save.IsEnabled ? 1 : 0.55;
                    hint.Visibility = stepTitle.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                };
                TrackComposition(stepRow);
                stepTitle.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.Key != Key.Enter || draftComposing || e.IsRepeat) return;
                    e.Handled = true;
                    if (FinishStep(false)) Render(false);
                };
                Render(false);
            }
            stepTitle.Focus();
            stepRow.BringIntoView();
        }

        private bool FinishStep(bool leaving)
        {
            if (stepRow == null) return true;
            string text = stepTitle.Text.Trim();
            if (text.Length == 0 && !leaving) { stepTitle.Focus(); return false; }
            if (text.Length > 0)
            {
                int before = stepParent.Steps.Count;
                bool wasDone = stepParent.Done;
                foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    if (!String.IsNullOrWhiteSpace(line))
                        stepParent.Steps.Add(new PlanStep { Title = line.Trim() });
                stepParent.Done = false;
                if (!Persist(this))
                {
                    stepParent.Steps.RemoveRange(before, stepParent.Steps.Count - before);
                    stepParent.Done = wasDone;
                    return false;
                }
            }
            ClearStep();
            return true;
        }

        private void ClearStep()
        {
            stepParent = null;
            stepRow = null;
            stepTitle = null;
            draftComposing = false;
        }

        private void CancelStep()
        {
            ClearStep();
            Render(false);
        }

        private void TrackComposition(FrameworkElement row)
        {
            TextCompositionManager.AddPreviewTextInputStartHandler(row, delegate { draftComposing = true; });
            TextCompositionManager.AddPreviewTextInputHandler(row, delegate { draftComposing = false; });
            row.LostKeyboardFocus += delegate { if (!row.IsKeyboardFocusWithin) draftComposing = false; };
        }

        private void BeginDraft()
        {
            if (draftRow == null)
            {
                if (!FinishStep(true)) return;
                draftDay = selected;
                draftRow = CreateDraftRow();
                Render(false);
            }
            draftTitle.Focus();
            draftRow.BringIntoView();
        }

        private Border CreateDraftRow()
        {
            var fields = new StackPanel();
            var row = new Border { Background = Paper, BorderBrush = TodayRing, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 6, 9, 6),
                Margin = new Thickness(0, 8, 0, 3), Child = fields };
            draftTitle = new TextBox { Template = InputSkin, FontSize = 14, Foreground = Ink, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Padding = new Thickness(2, 5, 2, 5) };
            AutomationProperties.SetName(draftTitle, "任务名称");
            var titleLine = new Grid();
            titleLine.Children.Add(draftTitle);
            var placeholder = Label("输入任务名称", 14, Muted);
            placeholder.Margin = new Thickness(4, 0, 0, 0);
            placeholder.IsHitTestVisible = false;
            titleLine.Children.Add(placeholder);
            fields.Children.Add(titleLine);
            draftHint = Label("请先填写任务名称", 12, Paint("#93483F"));
            draftHint.Visibility = Visibility.Collapsed;
            fields.Children.Add(draftHint);

            var stepLabel = Label("子任务（每行一项）", 12, Muted);
            stepLabel.Margin = new Thickness(2, 4, 0, 0);
            stepLabel.Visibility = Visibility.Collapsed;
            fields.Children.Add(stepLabel);
            draftNotes = new TextBox { Template = InputSkin, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinLines = 2,
                MaxLines = 4, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, FontSize = 13,
                Foreground = Ink, Background = Paper, Padding = new Thickness(5, 3, 5, 3),
                BorderBrush = Line, Margin = new Thickness(1, 4, 1, 6), Visibility = Visibility.Collapsed };
            AutomationProperties.SetName(draftNotes, "子任务（每行一项）");
            fields.Children.Add(draftNotes);
            var actions = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 4, 0, 0) };
            fields.Children.Add(actions);
            var submit = Button("保存任务", delegate { if (FinishDraft(false)) Render(false); }, Leaf, Brushes.White);
            submit.IsEnabled = false;
            submit.Opacity = 0.55;
            DockPanel.SetDock(submit, Dock.Right);
            actions.Children.Add(submit);
            var cancel = Button("取消", CancelDraft, null, Muted);
            DockPanel.SetDock(cancel, Dock.Right);
            actions.Children.Add(cancel);
            var addNotes = Button("添加子任务", delegate
            {
                stepLabel.Visibility = Visibility.Visible;
                draftNotes.Visibility = Visibility.Visible;
                draftNotes.Focus();
                row.BringIntoView();
            }, null, Leaf);
            addNotes.Click += delegate { addNotes.Visibility = Visibility.Collapsed; };
            actions.Children.Add(addNotes);
            draftTitle.TextChanged += delegate
            {
                placeholder.Visibility = draftTitle.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                submit.IsEnabled = !String.IsNullOrWhiteSpace(draftTitle.Text);
                submit.Opacity = submit.IsEnabled ? 1 : 0.55;
                if (submit.IsEnabled) draftHint.Visibility = Visibility.Collapsed;
            };
            TrackComposition(row);
            row.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter || draftComposing || e.IsRepeat) return;
                if (!draftTitle.IsKeyboardFocused && !(draftNotes.IsKeyboardFocused && Keyboard.Modifiers == ModifierKeys.Control)) return;
                e.Handled = true;
                if (FinishDraft(false)) Render(false);
            };
            return row;
        }

        private bool FinishDraft(bool leaving)
        {
            if (draftRow == null) return true;
            string name = draftTitle.Text.Trim(), notes = draftNotes.Text.Trim();
            if (name.Length == 0)
            {
                if (leaving && notes.Length == 0) { ClearDraft(); return true; }
                draftHint.Visibility = Visibility.Visible;
                draftTitle.Focus();
                draftTitle.BringIntoView();
                return false;
            }
            var task = new TaskItem { Id = Guid.NewGuid().ToString("N"), Day = DayKey(draftDay), Title = name, Notes = notes };
            store.Tasks.Add(task);
            if (!Persist(this)) { store.Tasks.Remove(task); return false; }
            ClearDraft();
            return true;
        }

        private void ClearDraft()
        {
            draftRow = null;
            draftTitle = null;
            draftNotes = null;
            draftHint = null;
            draftComposing = false;
        }

        private void CancelDraft()
        {
            ClearDraft();
            Render(false);
        }
    }
}
