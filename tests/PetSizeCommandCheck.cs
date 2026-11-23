using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Xiaohei
{
    internal static class PetSizeCommandCheck
    {
        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label);
            Console.WriteLine("PASS " + label);
        }

        [STAThread]
        private static int Main()
        {
            try { Run(); return 0; }
            catch (Exception ex)
            {
                while (ex != null)
                {
                    Console.Error.WriteLine(ex.GetType().Name + " " + ex.Message);
                    Console.Error.WriteLine(ex.StackTrace);
                    ex = ex.InnerException;
                }
                return 1;
            }
        }

        private static void Run()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var dir = Path.Combine("work", "size-test-" + Guid.NewGuid().ToString("N"));
            var pet = new DesktopPet(new Store(dir)) { Opacity = 0, ShowActivated = false };
            pet.Show();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { }));
            Check(!pet.IsActive, "test pet does not receive foreground focus");
            var sizes = pet.ContextMenu.Items.OfType<MenuItem>().Single(m => (string)m.Header == "桌宠大小");
            var trayField = typeof(DesktopPet).GetField("trayMenu", BindingFlags.NonPublic | BindingFlags.Instance);
            var tray = (ContextMenu)trayField.GetValue(pet);
            var traySizes = tray.Items.OfType<MenuItem>().Single(m => (string)m.Header == "桌宠大小");
            foreach (var option in sizes.Items.Cast<MenuItem>().Concat(traySizes.Items.Cast<MenuItem>()))
            {
                option.ApplyTemplate();
                var peer = UIElementAutomationPeer.CreatePeerForElement(option);
                var command = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke);
                command.Invoke();
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { }));
                Check(pet.Width == (int)option.Tag && new Store(dir).PetWidth == pet.Width,
                    "menu command applied and saved " + option.Tag);
            }
            var wheel = typeof(DesktopPet).GetMethod("AdjustPetWheel", BindingFlags.Instance | BindingFlags.NonPublic);
            wheel.Invoke(pet, new object[] { 15 });
            Check(pet.Width == 192, "partial wheel increment held");
            wheel.Invoke(pet, new object[] { 15 });
            Check(Math.Abs(pet.Width - 193) < 1 && new Store(dir).PetWidth == 193, "precision scroll accumulates and persists");
            wheel.Invoke(pet, new object[] { -120 });
            Check(Math.Abs(pet.Width - 189) < 1 && new Store(dir).PetWidth == 189, "standard scroll moves four units");
            var restored = new DesktopPet(new Store(dir)) { Opacity = 0, ShowActivated = false };
            restored.Show();
            Check(Math.Abs(restored.Width - pet.Width) < 1 && Math.Abs(restored.Left - pet.Left) < 1
                && Math.Abs(restored.Top - pet.Top) < 1,
                "size and position restored on a second instance");
            pet.Close();
            restored.Close();
            app.Shutdown();
        }
    }
}
