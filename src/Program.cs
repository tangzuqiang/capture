using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Shiying
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

            if (args != null && args.Length == 3 && args[0] == "--ocr")
            {
                Environment.Exit(RunOcrFile(args[1], args[2]));
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
            {
                MessageBox.Show(e.Exception.Message, "拾影", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        static int RunOcrFile(string imagePath, string outputPath)
        {
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                string text;
                using (var bitmap = new System.Drawing.Bitmap(imagePath))
                {
                    text = OcrText.Recognize(bitmap);
                }

                File.WriteAllText(outputPath, text + "\r\n\r\n[" + watch.ElapsedMilliseconds + " ms]\r\n", Encoding.UTF8);
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(outputPath, ex.ToString(), Encoding.UTF8);
                return 1;
            }
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
