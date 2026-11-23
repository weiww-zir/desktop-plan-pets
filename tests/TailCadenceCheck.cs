using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Xiaohei
{
    internal static class TailCadenceCheck
    {
        static void PumpUntil(Func<bool> predicate, int timeoutMs)
        {
            var elapsed = Stopwatch.StartNew();
            while (!predicate())
            {
                if (elapsed.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Expected state was not reached");
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { }));
                Thread.Sleep(12);
            }
        }

        static DispatcherTimer Timer(PetActor pet, string field)
        {
            return (DispatcherTimer)typeof(PetActor).GetField(field,BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pet);
        }

        [STAThread]
        static void Main()
        {
            var pet = new PetActor();
            var host = new Window { Content = pet, ShowInTaskbar = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Width = 92, Height = 92, Left = -10000, Top = -10000 };
            host.Show();
            PumpUntil(delegate { return pet.IsLoaded; }, 3000);
            var elapsed = Stopwatch.StartNew();
            PumpUntil(delegate { return pet.Pose == PetPose.TailKittens; }, 33500);
            double first = elapsed.Elapsed.TotalSeconds;
            Console.WriteLine("First automatic play at {0:F2}s",first);
            PumpUntil(delegate { return pet.Pose == PetPose.Rest; }, 8500);
            pet.Meow();
            var idle = Timer(pet,"idle");
            idle.Interval = TimeSpan.FromMilliseconds(1450);
            PumpUntil(delegate { return pet.Pose == PetPose.Nap; }, 2100);
            idle.Interval = TimeSpan.FromMinutes(2);
            PumpUntil(delegate { return pet.Pose == PetPose.TailKittens; }, 25500);
            double second = elapsed.Elapsed.TotalSeconds;
            Console.WriteLine("Second automatic play at {0:F2}s, interval {1:F2}s",second,second-first);
            if (Math.Abs(first-30) > 1.5 || Math.Abs(second-first-30) > 1.5) throw new Exception("Wrong interval");
            PumpUntil(delegate { return pet.Pose == PetPose.Nap; }, 8600);
            var cadence = Timer(pet,"tailTimer");
            cadence.Interval = TimeSpan.FromSeconds(1);
            var manualClock = Stopwatch.StartNew();
            double tickAfterManual = -1;
            cadence.Tick += delegate { tickAfterManual = manualClock.Elapsed.TotalSeconds; };
            PumpUntil(delegate { return manualClock.ElapsedMilliseconds >= 500; }, 1000);
            manualClock.Restart();
            pet.SplitTail();
            if (pet.Pose != PetPose.TailKittens) throw new Exception("Manual play failed");
            PumpUntil(delegate { return tickAfterManual >= 0; }, 1550);
            if (tickAfterManual < .92) throw new Exception("Manual play did not restart countdown");
            host.Close();
            PumpUntil(delegate { return !cadence.IsEnabled; }, 2000);
            Console.WriteLine("PASS returned to nap, manual replay reset countdown, unloading stopped timer");
        }
    }
}
