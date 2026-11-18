using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Jietu
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            NativeMethods.MakeDpiAware();

            if (HasArg(args, "--selftest"))
            {
                Environment.Exit(RunLogged("bin\\selftest.log", SelfTest.Run));
            }

            if (HasArg(args, "--uitest"))
            {
                Environment.Exit(RunLogged("bin\\uitest.log", UiTest.Run));
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
            {
                MessageBox.Show(e.Exception.Message, "截长图", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.Run(new MainForm());
        }

        static int RunLogged(string path, Func<int> test)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var log = new StringWriter();
            TextWriter previous = Console.Out;
            Console.SetOut(log);
            int code;
            try
            {
                code = test();
            }
            catch (Exception ex)
            {
                log.WriteLine(ex);
                code = 1;
            }
            finally
            {
                Console.SetOut(previous);
            }

            File.WriteAllText(path, log.ToString(), Encoding.UTF8);
            try
            {
                Console.Write(log.ToString());
            }
            catch (IOException)
            {
            }

            return code;
        }

        static bool HasArg(string[] args, string name)
        {
            if (args == null)
            {
                return false;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
