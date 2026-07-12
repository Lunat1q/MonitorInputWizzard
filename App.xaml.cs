using System.IO;
using System.Windows;

namespace MonitorInputWizzard
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Headless mode: `MonitorInputWizzard.exe <input name>` switches and exits, no window.
            string? target = ParseTarget(e.Args);
            if (target is not null)
            {
                HeadlessSwitch(target);
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (_, args) =>
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "miw-crash.txt"), args.Exception.ToString());
                MessageBox.Show(args.Exception.ToString(), "Crash");
                args.Handled = true;
            };
            new MainWindow().Show();
        }

        // Accepts "HDMI1", "HDMI 1", or "--switch HDMI1". Returns null if no switch requested.
        private static string? ParseTarget(string[] args)
        {
            if (args.Length == 0) return null;
            var parts = args[0] is "--switch" or "-s" ? args.Skip(1) : args;
            var name = string.Join(" ", parts).Trim();
            return name.Length == 0 ? null : name;
        }

        private static string Norm(string s) => s.Replace(" ", "").ToLowerInvariant();

        private static void HeadlessSwitch(string name)
        {
            var s = Settings.Load();
            var preset = s.Inputs.FirstOrDefault(i => Norm(i.Name) == Norm(name));
            if (preset is null) return;

            if (s.UseNvApi)
            {
                using var nv = new NvApi();
                nv.SetVcp(s.InputRegister, (ushort)preset.Code, s.SourceAddr);
            }
            else
            {
                using var mc = new MonitorController();
                var mon = mc.Enumerate().FirstOrDefault(m => m.Description == s.MonitorDescription);
                if (mon.Handle != IntPtr.Zero) mc.SetVcp(mon.Handle, s.InputRegister, preset.Code);
            }
        }
    }
}
