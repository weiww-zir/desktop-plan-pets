using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Xiaohei
{
    internal static class TypingCheck
    {
        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
        }

        static T Field<T>(PetActor actor, string name)
        {
            return (T)typeof(PetActor).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(actor);
        }

        static void Until(Func<bool> reached, string description, int timeout = 2400)
        {
            var time = Stopwatch.StartNew();
            while (!reached())
            {
                if (time.ElapsedMilliseconds > timeout) throw new Exception("Timeout: " + description);
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate {}));
                Thread.Sleep(3);
            }
        }

        static void Wait(int ms)
        {
            var time = Stopwatch.StartNew();
            Until(() => time.ElapsedMilliseconds >= ms, "dispatcher delay");
        }

        static void Play()
        {
            var actor = new PetActor();
            var window = new Window { Width = 190, Height = 200, Content = actor, ShowActivated = false,
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -9000, Top = -9000 };
            var finish = Field<DispatcherTimer>(actor, "finish");
            var idle = Field<DispatcherTimer>(actor, "idle");
            var hover = Field<DispatcherTimer>(actor, "hover");
            var tail = Field<DispatcherTimer>(actor, "tailTimer");
            actor.TapKeyboard();
            Check(actor.Pose == PetPose.Rest && !finish.IsEnabled, "unloaded actor ignores queued keyboard input");
            window.Show();
            Until(() => actor.IsLoaded && actor.IsVisible, "actor load");
            actor.ShowPendingCount(3);
            actor.TapKeyboard();
            var keyboard = Field<PetArt.TypingKeyboard>(actor, "typingKeyboard");
            Check(actor.Pose == PetPose.Typing && keyboard != null && !Field<bool>(actor, "nextStrokeLeft"),
                "first pulse immediately taps the left paw");
            for (int i = 0; i < 20; i++) actor.TapKeyboard();
            Check(!Field<bool>(actor, "nextStrokeLeft"), "rapid pulses do not queue extra strikes");
            Wait(120);
            actor.TapKeyboard();
            Check(Field<bool>(actor, "nextStrokeLeft") && keyboard == Field<PetArt.TypingKeyboard>(actor, "typingKeyboard"),
                "a later pulse taps the other paw in the same drawing");
            Wait(46);
            actor.TapKeyboard();
            var pause = Stopwatch.StartNew();
            Check(Field<bool>(actor, "nextStrokeLeft"), "last pulse falls inside the visual throttle");
            Until(() => actor.Pose == PetPose.Rest, "return to seated rest");
            Check(pause.ElapsedMilliseconds >= 778 && pause.ElapsedMilliseconds < 1600 && !finish.IsEnabled,
                "the last suppressed pulse renews the pause and no queued motion is played");

            var normalIdle = idle.Interval;
            idle.Interval = TimeSpan.FromMilliseconds(35);
            Until(() => actor.Pose == PetPose.Nap, "nap");
            idle.Interval = normalIdle;
            actor.TapKeyboard();
            Check(actor.Pose == PetPose.Typing, "typing wakes the sleeping cat");
            actor.Celebrate(true);
            actor.TapKeyboard();
            Check(actor.Pose == PetPose.Wave, "completion interrupts typing and keeps the wave");
            finish.Interval = TimeSpan.FromMilliseconds(40);
            Until(() => actor.Pose == PetPose.Hop, "celebration hop");
            actor.TapKeyboard();
            Check(actor.Pose == PetPose.Hop, "keyboard activity does not interrupt the follow-up hop");
            finish.Interval = TimeSpan.FromMilliseconds(40);
            Until(() => actor.Pose == PetPose.Rest, "celebration finish");
            actor.WatchSpirits();
            var normalTail = tail.Interval;
            tail.Interval = TimeSpan.FromMilliseconds(45);
            Wait(110);
            tail.Interval = normalTail;
            actor.TapKeyboard();
            Check(actor.Pose == PetPose.SpiritOrbit, "typing and the scheduled tail wait for the butterflies");
            finish.Interval = TimeSpan.FromMilliseconds(40);
            Until(() => actor.Pose == PetPose.TailKittens, "pending tail after butterflies");
            actor.TapKeyboard();
            Check(actor.Pose == PetPose.TailKittens, "typing leaves the kittens in place");
            finish.Interval = TimeSpan.FromMilliseconds(40);
            Until(() => actor.Pose == PetPose.Rest, "kittens finish");
            actor.TapKeyboard();
            window.Content = null;
            Until(() => !actor.IsLoaded, "unload during typing");
            actor.TapKeyboard();
            Check(!finish.IsEnabled && !idle.IsEnabled && !hover.IsEnabled && !tail.IsEnabled, "unloaded actor stops all its timers");
            window.Content = actor;
            Until(() => actor.IsLoaded && actor.Pose == PetPose.Rest, "reload without stale typing");
            window.Close();
        }

        static BitmapSource Capture(FrameworkElement scene, int width, int height)
        {
            scene.Measure(new Size(width, height));
            scene.Arrange(new Rect(0, 0, width, height));
            scene.UpdateLayout();
            var frame = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            frame.Render(scene);
            return frame;
        }

        static double CenterOfChange(BitmapSource baseline, BitmapSource hit)
        {
            int width = baseline.PixelWidth;
            var a = new byte[width * 4 * baseline.PixelHeight];
            var b = new byte[a.Length];
            baseline.CopyPixels(a, width * 4, 0);
            hit.CopyPixels(b, width * 4, 0);
            int count = 0;
            double center = 0;
            for (int p = 0; p < a.Length; p += 4)
            {
                int difference = 0;
                for (int channel = 0; channel < 4; channel++) difference += Math.Abs(a[p+channel] - b[p+channel]);
                if (difference < 20) continue;
                if (p / (4 * width) < baseline.PixelHeight * .56) throw new Exception("Stroke changed the face");
                center += (p / 4) % width;
                count++;
            }
            if (count == 0) throw new Exception("Missing visible key/paw contact at width " + width);
            return center / count;
        }

        static void Frames(string dir)
        {
            foreach (int width in new[] { 50, 92, 240 })
            {
                int height = (int)Math.Round(width * 455.0 / 480);
                var anchor = new FrameworkElement();
                var keyboard = new PetArt.TypingKeyboard(false);
                var scene = PetArt.Draw(false, PetPose.Typing, anchor, keyboard);
                var up = Capture(scene, width, height);
                keyboard.SetDepth(true, 1);
                var left = Capture(scene, width, height);
                keyboard.SetDepth(true, 0);
                keyboard.SetDepth(false, 1);
                var right = Capture(scene, width, height);
                var ear = anchor.TranslatePoint(new Point(), scene);
                Check(CenterOfChange(up, left) < CenterOfChange(up, right) && ear.X > width * .72 && ear.Y < height * .23,
                    "distinct left/right contact and stable right-ear anchor at " + width + " DIPs");
                if (width != 240 || dir == null) continue;
                var preview = new DrawingVisual();
                using (var draw = preview.RenderOpen())
                {
                    var frames = new[] { up, left, right };
                    for (int i = 0; i < frames.Length; i++) draw.DrawImage(frames[i], new Rect(i * width, 0, width, height));
                }
                var result = new RenderTargetBitmap(width * 3, height, 96, 96, PixelFormats.Pbgra32);
                result.Render(preview);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(result));
                Directory.CreateDirectory(dir);
                using (var file = File.Create(Path.Combine(dir, "TypingPaws.png"))) png.Save(file);
            }
        }

        [STAThread] static int Main(string[] args)
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Frames(args.Length == 0 ? null : args[0]);
                Play();
                app.Shutdown();
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
