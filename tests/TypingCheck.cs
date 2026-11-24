using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            actor.TapKeyboard("C");
            Check(actor.Pose == PetPose.Rest && !finish.IsEnabled, "unloaded actor ignores keyboard input");
            window.Show();
            Until(() => actor.IsLoaded && actor.IsVisible, "actor load");
            actor.ShowPendingCount(3);
            actor.TapKeyboard("C");
            var keyboard = Field<PetArt.TypingKeyboard>(actor, "typingKeyboard");
            Check(actor.Pose == PetPose.Typing && keyboard != null && Caption(keyboard).Text == "C",
                "first keystroke immediately shows C");
            foreach (string key in new[] { "A", "T", "P", "A", "P", "P", "4", ";", "Space", "F12" })
            {
                actor.TapKeyboard(key);
                Check(keyboard == Field<PetArt.TypingKeyboard>(actor, "typingKeyboard") && Caption(keyboard).Text == key,
                    "rapid keystroke shows the latest key in the same drawing: " + key);
            }
            actor.TapKeyboard();
            Check(string.IsNullOrEmpty(Caption(keyboard).Text), "anonymous keystroke clears the old key");
            actor.TapKeyboard("C");
            actor.TapKeyboard("UnknownKey");
            Check(string.IsNullOrEmpty(Caption(keyboard).Text), "unrecognized key does not leave or invent a label");
            actor.TapKeyboard("T");
            Wait(360);
            actor.TapKeyboard("T");
            var pause = Stopwatch.StartNew();
            Wait(520);
            Check(actor.Pose == PetPose.Typing && Caption(keyboard).Text == "T", "rebound retains the last key until the renewed pause ends");
            Until(() => actor.Pose == PetPose.Rest, "return to seated rest");
            Check(pause.ElapsedMilliseconds >= 775 && pause.ElapsedMilliseconds < 1450 &&
                !finish.IsEnabled && !Descendants<PetArt.TypingKeyboard>(actor).Any(k => k.IsVisible),
                "stopping typing removes the keyboard from the pet after about 800 ms");

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
            actor.TapKeyboard("A");
            Check(actor.Pose == PetPose.Typing, "keystroke immediately interrupts the scheduled kittens");
            tail.Interval = TimeSpan.FromMilliseconds(35);
            Wait(100);
            tail.Interval = normalTail;
            Check(actor.Pose == PetPose.Typing, "an automatic tail tick leaves active typing visible");
            actor.SplitTail();
            actor.TapKeyboard("C");
            Check(actor.Pose == PetPose.TailKittens, "typing honours a manually requested tail animation");
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

        static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T) yield return (T)child;
                foreach (var element in Descendants<T>(child)) yield return element;
            }
        }

        static TextBlock Caption(PetArt.TypingKeyboard keyboard)
        {
            return Descendants<Viewbox>(keyboard).Select(v => v.Child).OfType<TextBlock>().Single();
        }

        static int Differences(BitmapSource first, BitmapSource second, Rect area)
        {
            int width = first.PixelWidth;
            var a = new byte[width * 4 * first.PixelHeight];
            var b = new byte[a.Length];
            first.CopyPixels(a, width * 4, 0);
            second.CopyPixels(b, width * 4, 0);
            int count = 0;
            for (int y = Math.Max(0, (int)area.Top); y < Math.Min(first.PixelHeight, (int)Math.Ceiling(area.Bottom)); y++)
                for (int x = Math.Max(0, (int)area.Left); x < Math.Min(width, (int)Math.Ceiling(area.Right)); x++)
                {
                    int p = (y * width + x) * 4;
                    int change = 0;
                    for (int c = 0; c < 4; c++) change += Math.Abs(a[p + c] - b[p + c]);
                    if (change > 40) count++;
                }
            return count;
        }

        static int FurPixels(BitmapSource image, Rect area)
        {
            var crop = new CroppedBitmap(image, new Int32Rect((int)area.X, (int)area.Y,
                (int)area.Width, (int)area.Height));
            int stride = crop.PixelWidth * 4;
            var pixels = new byte[stride * crop.PixelHeight];
            crop.CopyPixels(pixels, stride, 0);
            int count = 0;
            for (int i = 0; i < pixels.Length; i += 4)
                if (pixels[i+3] == 255 && Math.Max(pixels[i], Math.Max(pixels[i+1], pixels[i+2])) < 40) count++;
            return count;
        }

        static void AnimatedRebound()
        {
            const int width = 240, height = 228;
            var keyboard = new PetArt.TypingKeyboard(true);
            var scene = PetArt.Draw(false, PetPose.Typing, null, keyboard);
            var host = new Window { Content = scene, Width = width, Height = height, WindowStyle = WindowStyle.None,
                ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -9000, Top = -9000 };
            host.Show();
            var raised = Capture(scene, width, height);
            Rect a, p;
            if (!PetArt.TypingKeyboard.TryGetKeyBounds("A", out a) || !PetArt.TypingKeyboard.TryGetKeyBounds("P", out p))
                throw new Exception("Missing A or P from the drawn keyboard");
            var transform = keyboard.TransformToAncestor(scene);
            a = transform.TransformBounds(a);
            p = transform.TransformBounds(p);
            keyboard.Press("A", true);
            int restingFur = FurPixels(raised, a);
            Until(() => FurPixels(Capture(scene, width, height), a) > restingFur + 2,
                "animated A paw touches the cap before striking P");
            keyboard.Press("P", false);
            Until(delegate {
                var overlap = Capture(scene, width, height);
                return Differences(raised, overlap, a) >= 4 && Differences(raised, overlap, p) >= 4;
            }, "P makes contact while A is still releasing");
            Until(delegate {
                var released = Capture(scene, width, height);
                return Differences(raised, released, a) == 0 && Differences(raised, released, p) == 0;
            }, "both A and P rebound on their own");
            Check(Caption(keyboard).Text == "P", "animated rapid cross-hand press rebounds while P stays readable");
            Rect enter;
            if (!PetArt.TypingKeyboard.TryGetKeyBounds("Enter", out enter)) throw new Exception("Missing Enter key");
            enter = transform.TransformBounds(enter);
            keyboard.Press("Enter", true);
            keyboard.Hold("Enter");
            Wait(320);
            var held = Capture(scene, width, height);
            Check(keyboard.IsKeyHeld && keyboard.StrikeSide == 0 && Differences(raised, held, enter) > 4,
                "tail stays on the physically held Enter key past the stroke duration");
            keyboard.Release("A");
            Check(keyboard.IsKeyHeld, "unrelated key-up keeps Enter held");
            keyboard.Release("Enter");
            Until(() => Differences(raised, Capture(scene, width, height), enter) == 0, "tail released Enter");
            Check(!keyboard.IsKeyHeld && Caption(keyboard).Text == "Enter", "Enter release relaxes the tail and preserves the last label");
            var leftTip = transform.TransformBounds(new Rect(215,392,78,27));
            Rect space;
            if (!PetArt.TypingKeyboard.TryGetKeyBounds("Space", out space)) throw new Exception("Missing Space key");
            space = transform.TransformBounds(space);
            keyboard.Press("Space", true);
            keyboard.Hold("Space");
            keyboard.Release("Space");
            Until(() => Differences(raised, Capture(scene, width, height), leftTip) > 25 &&
                Differences(raised, Capture(scene, width, height), space) > 3, "sub-frame Space tap makes visible contact");
            Until(() => Differences(raised, Capture(scene, width, height), leftTip) == 0,
                "short Space tap returns the left tip home");
            keyboard.Press("Space", true);
            keyboard.Hold("Space");
            Wait(130);
            var onSpace = Capture(scene, width, height);
            keyboard.Press("Enter", true);
            keyboard.Hold("Enter");
            Check(Differences(onSpace, Capture(scene, width, height), leftTip) < 12,
                "switching held special keys keeps the visible left curl extended");
            Wait(130);
            var onEnter = Capture(scene, width, height);
            Check(Differences(onSpace, onEnter, enter) > 3 && Differences(raised, onEnter, leftTip) > 25,
                "the extended left tip moves from Space to Enter");
            keyboard.Release("Enter");
            Until(() => Differences(raised, Capture(scene, width, height), leftTip) == 0,
                "switched special keys restore one seated tip");
            host.Close();
        }

        static void Frames(string dir)
        {
            foreach (int width in new[] { 64, 92, 240 })
            {
                int height = (int)Math.Round(width * 455.0 / 480);
                var anchor = new FrameworkElement();
                var keyboard = new PetArt.TypingKeyboard(false);
                var scene = PetArt.Draw(false, PetPose.Typing, anchor, keyboard);
                Capture(scene, width, height);
                var restAnchor = new FrameworkElement();
                var idleView = PetArt.Draw(false, PetPose.Rest, restAnchor);
                var sitting = Capture(idleView, width, height);
                var ear = anchor.TranslatePoint(new Point(), scene);
                var idleEar = restAnchor.TranslatePoint(new Point(), idleView);
                Check((ear - idleEar).Length < .2 && ear.X > width * .72 && ear.Y < height * .23,
                    "typing keeps the seated right-ear anchor at " + width + " DIPs");
                var transforms = keyboard.TransformToAncestor(scene);
                var keys = new[] { "C", "A", "P" };
                var images = new List<BitmapSource>();
                var bounds = new List<Rect>();
                foreach (string key in keys)
                {
                    Rect cap;
                    Check(PetArt.TypingKeyboard.TryGetKeyBounds(key, out cap), "drawn key exists: " + key);
                    var actual = transforms.TransformBounds(cap);
                    bounds.Add(actual);
                    keyboard.Press(key, false);
                    keyboard.SetPressDepth(0);
                    var released = Capture(scene, width, height);
                    keyboard.SetPressDepth(1);
                    var pressed = Capture(scene, width, height);
                    images.Add(pressed);
                    Check(Differences(released, pressed, actual) > 0,
                        key + " has a visible press at its own keycap at " + width + " DIPs");
                    keyboard.SetPressDepth(0);
                    var rebound = Capture(scene, width, height);
                    Check(Differences(released, rebound, new Rect(0, 0, width, height)) == 0,
                        key + " returns to the released frame");
                    var caption = Caption(keyboard);
                    var captionBounds = caption.TransformToAncestor(scene).TransformBounds(new Rect(caption.RenderSize));
                    Check(caption.Text == key && new Rect(0, 0, width, height).Contains(captionBounds) &&
                        (width != 92 || captionBounds.Height >= 12),
                        key + " caption fits the frame and is readable at default size; " + captionBounds);
                    if (width == 92)
                    {
                        caption.Visibility = Visibility.Hidden;
                        var withoutLetter = Capture(scene, width, height);
                        caption.Visibility = Visibility.Visible;
                        int ink = Differences(rebound, withoutLetter, captionBounds);
                        Check(ink >= 14, key + " paints a visible single glyph at the default pet size: " + ink + " pixels");
                    }
                    Check(Differences(sitting, pressed, new Rect(0, 0, width * .75, height * .62)) == 0,
                        "key feedback preserves the seated face");
                }
                Check(bounds[1].Right < bounds[0].Left && bounds[0].Right < bounds[2].Left,
                    "A and C have distinct left-hand positions and P is on the right");
                var pawBand = transforms.TransformBounds(new Rect(190, 370, 400, 55));
                Check(Differences(images[0], images[1], pawBand) > 0 && Differences(images[1], images[2], pawBand) > 0,
                    "same-side and cross-side presses visibly move the paws at " + width + " DIPs");
                keyboard.Press(null, false);
                Check(string.IsNullOrEmpty(Caption(keyboard).Text), "anonymous press erases the previous caption");
                if (width != 240 || dir == null) continue;
                var preview = new DrawingVisual();
                using (var draw = preview.RenderOpen())
                    for (int i = 0; i < images.Count; i++) draw.DrawImage(images[i], new Rect(i * width, 0, width, height));
                var result = new RenderTargetBitmap(width * images.Count, height, 96, 96, PixelFormats.Pbgra32);
                result.Render(preview);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(result));
                Directory.CreateDirectory(dir);
                using (var file = File.Create(Path.Combine(dir, "TypingPaws.png"))) png.Save(file);
            }
        }

        static void DeskActions()
        {
            const int width = 480, height = 455;
            var keys = new PetArt.TypingKeyboard(false);
            var scene = PetArt.Draw(false, PetPose.Typing, null, keys);
            var neutral = Capture(scene, width, height);
            var map = keys.TransformToAncestor(scene);
            var board = map.TransformBounds(new Rect(198, 419, 366, 81));
            var leftTip = map.TransformBounds(new Rect(215, 392, 78, 27));
            var rightRoot = map.TransformBounds(new Rect(460, 382, 163, 35));
            var originalTip = Descendants<System.Windows.Shapes.Path>(scene).Single(path =>
                path.Data.Bounds.Left > 260 && path.Data.Bounds.Left < 270 && path.Data.Bounds.Right == 320);
            var restingOutline = originalTip.Data;
            var mouse = map.TransformBounds(new Rect(570, 430, 64, 68));
            foreach (string key in new[] { "Space", "Enter", "Bksp", "Esc" })
            {
                keys.Press(key, true);
                var down = Capture(scene, width, height);
                Rect cap;
                if (!PetArt.TypingKeyboard.TryGetKeyBounds(key, out cap)) throw new Exception("Missing " + key);
                Check(keys.StrikeSide == 0 && Caption(keys).Text == key && Differences(neutral, down, leftTip) >= 180,
                    key + " unfurls the short left tail tip with reduced motion");
                var onCap = map.TransformBounds(cap);
                Check(FurPixels(down, onCap) > FurPixels(neutral, onCap) + 4 && originalTip.Data != restingOutline,
                    key + " moves the original tip and paints black fur on the cap");
                Check(Differences(neutral, down, rightRoot) == 0, key + " leaves the seated right tail silhouette intact");
                keys.SetPressDepth(.5);
                var halfway = Capture(scene, width, height);
                Check(Differences(neutral, halfway, leftTip) > 10 && Differences(halfway, down, leftTip) > 10,
                    key + " bends the left silhouette progressively");
                keys.Rest();
                var tipUp = Capture(scene, width, height);
                Check(originalTip.Data == restingOutline && Differences(neutral, tipUp, board) == 0 && Differences(neutral, tipUp, leftTip) == 0,
                    key + " restores the original tip and releases the keyboard completely");
            }
            keys.Press("A", true);
            keys.Mouse(0, 0, 0, 0);
            var reach = Capture(scene, width, height);
            Check(keys.StrikeSide == 1 && Caption(keys).Text == "" && Differences(neutral, reach, mouse) >= 15,
                "pointer motion clears the letter and reaches a separate mouse with the right paw");
            keys.Rest();
            keys.Mouse(0, 0, 1, 0);
            var left = Capture(scene, width, height);
            keys.Rest();
            keys.Mouse(0, 0, 2, 0);
            var right = Capture(scene, width, height);
            Check(Differences(left, right, map.TransformBounds(new Rect(580, 440, 42, 26))) >= 20,
                "left and right mouse presses light different halves of the mouse");
            keys.Rest();
            keys.Mouse(0, 0, 4, 1);
            var up = Capture(scene, width, height);
            keys.Mouse(0, 0, 4, -1);
            var downWheel = Capture(scene, width, height);
            Check(Differences(up, downWheel, mouse) >= 4, "wheel feedback distinguishes scroll direction");
            keys.Press("P", true);
            Check(keys.StrikeSide == -1 && Caption(keys).Text == "P", "left paw handles typing while right paw holds the mouse");
            keys.Press("U", false);
            Check(keys.StrikeSide == 0 && Caption(keys).Text == "U", "tail helps when the mouse and left paw are occupied");
            keys.Press(null, true);
            keys.Mouse(1, -1, 0, 0);
            Check(Caption(keys).Text == "", "switching to anonymous input and mouse leaves no old letter");
            keys.Rest();
            keys.Press("A", true);
            keys.Hold("A");
            keys.Press("P", true);
            keys.Hold("P");
            keys.Release("A");
            Check(keys.IsKeyHeld && keys.StrikeSide == 1 && Caption(keys).Text == "P", "only the latest physical hold is retained");
            keys.Release(null);
            Check(!keys.IsKeyHeld && Caption(keys).Text == "", "anonymous release erases a held key");
            foreach (string heldKey in new[] { "Shift", "A", "P", "Space" })
            {
                keys.Rest();
                keys.Press(heldKey, true);
                keys.Hold(heldKey);
                keys.Mouse(1, 1, 0, 0);
                keys.Mouse(1, 1, 1, 0);
                Rect cap;
                if (!PetArt.TypingKeyboard.TryGetKeyBounds(heldKey, out cap)) throw new Exception("Missing " + heldKey);
                var heldArea = map.TransformBounds(cap);
                var duringClick = Capture(scene, width, height);
                Check(keys.IsKeyHeld && Caption(keys).Text == "" && Differences(neutral, duringClick, heldArea) >= 4,
                    heldKey + " stays physically pressed during mouse motion and a click without repeating the letter");
                keys.Release(heldKey);
                Check(!keys.IsKeyHeld && Differences(duringClick, Capture(scene, width, height), heldArea) >= 4,
                    "matching key-up releases " + heldKey + " while the mouse is still held");
            }
            keys.Rest();
            Check(Differences(neutral, Capture(scene, width, height), new Rect(0, 0, width, height)) == 0,
                "keyboard, mouse, both paws and tail return to their original desk layout");
        }

        [STAThread] static int Main(string[] args)
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Frames(args.Length == 0 ? null : args[0]);
                DeskActions();
                AnimatedRebound();
                Play();
                app.Shutdown();
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
