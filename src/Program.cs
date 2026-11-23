using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Shiying.Voice;

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

            if (HasArg(args, "--updatecheck"))
            {
                Environment.Exit(RunLogged("bin\\updatecheck.log", RunUpdateCheck));
            }

            if (HasArg(args, "--updatedownload"))
            {
                Environment.Exit(RunLogged("bin\\updatedownload.log", RunUpdateDownload));
            }

            // 只为了本地看更新对话框（不点「立即更新」不会安装）
            if (HasArg(args, "--updatewindow"))
            {
                try
                {
                    Application.EnableVisualStyles();
                    UpdateInfo info = Updater.Fetch();
                    using (var form = new UpdateForm(info))
                    {
                        Application.Run(form);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "拾影", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return;
            }

            if (args != null && args.Length == 3 && args[0] == "--ocr")
            {
                Environment.Exit(RunOcrFile(args[1], args[2]));
            }

            // 只为了本地看模型下载框（不点「开始下载」不会联网）
            if (HasArg(args, "--voicemodelsui"))
            {
                try
                {
                    Application.EnableVisualStyles();
                    using (var form = new VoiceModelForm(VoiceModelInstaller.TargetDir()))
                    {
                        Application.Run(form);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "拾影", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return;
            }

            // 命令行下载语音模型：--voicemodels [目录]，不带目录就用默认位置
            if (args != null && HasArg(args, "--voicemodels"))
            {
                string dir = args.Length >= 2 && !args[1].StartsWith("-", StringComparison.Ordinal) ? args[1] : null;
                Environment.Exit(RunLogged("bin\\voicemodels.log", () => RunVoiceModels(dir)));
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

        // 命令行自检：拉一次 latest.json，输出本地/远端版本，方便在服务器或本机核对发布是否生效。
        static int RunUpdateCheck()
        {
            try
            {
                UpdateInfo info = Updater.Fetch();
                string current = AppInfo.Version;
                bool newer = Updater.IsNewer(info.Version, current);
                Console.WriteLine("current=" + (current.Length > 0 ? current : "?"));
                Console.WriteLine("remote=" + info.Version);
                Console.WriteLine("newer=" + newer);
                Console.WriteLine("size=" + info.Size);
                Console.WriteLine("sha256=" + info.Sha256);
                Console.WriteLine("url=" + info.Url);
                Console.WriteLine("notes=" + info.Notes);
                return newer ? 2 : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR " + ex.GetType().Name + " " + ex.Message);
                return 1;
            }
        }

        // 命令行自检：真正下载一次安装包并校验 sha256（不启动安装），用来验证发布链路。
        static int RunUpdateDownload()
        {
            try
            {
                UpdateInfo info = Updater.Fetch();
                string error;
                if (!Updater.Download(info, null, null, out error))
                {
                    Console.WriteLine("FAIL " + error);
                    return 1;
                }

                Console.WriteLine("OK " + Updater.InstallerPath + " " + info.Size + " bytes");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR " + ex.GetType().Name + " " + ex.Message);
                return 1;
            }
        }

        // 命令行入口：和托盘菜单「下载语音模型…」走同一套逻辑，方便脚本/绿色版批量装。
        static int RunVoiceModels(string dir)
        {
            try
            {
                string models = string.IsNullOrEmpty(dir) ? Shiying.Voice.VoiceModelInstaller.TargetDir() : Path.GetFullPath(dir);
                Console.WriteLine("models=" + models);
                if (Shiying.Voice.VoiceModelInstaller.Ready(models))
                {
                    Console.WriteLine("OK already ready");
                    return 0;
                }

                long lastPercent = -1;
                Shiying.Voice.VoiceModelInstaller.Install(models,
                    text => Console.WriteLine(text),
                    (done, total) =>
                    {
                        long percent = total > 0 ? done * 100 / total : -1;
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            Console.WriteLine(percent >= 0
                                ? "  " + percent + "% " + (done / 1048576.0).ToString("0.0") + "/" + (total / 1048576.0).ToString("0.0") + " MB"
                                : "  " + (done / 1048576.0).ToString("0.0") + " MB");
                        }
                    },
                    () => false);

                Console.WriteLine(Shiying.Voice.VoiceModelInstaller.Ready(models) ? "OK" : "FAIL models incomplete");
                return Shiying.Voice.VoiceModelInstaller.Ready(models) ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR " + ex.GetType().Name + " " + ex.Message);
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
