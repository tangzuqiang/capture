using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;

namespace Shiying.Voice
{
    // 语音模型不在安装包里：第一次用语音助手时按需下载到可写目录。
    //   Vosk  small Chinese  : 命令识别（~42 MB，zip）
    //   VITS  fanchen-C      : 本地朗读（~114 MB，tar.bz2）
    // 和 scripts\fetch-voice-models.ps1 用同一套地址、同一个目录结构，两边下的模型可以互换。
    static class VoiceModelInstaller
    {
        public const string VoskUrl = "https://alphacephei.com/vosk/models/vosk-model-small-cn-0.22.zip";
        public const string VitsUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/vits-zh-hf-fanchen-C.tar.bz2";
        const long VoskBytes = 43898754L;
        const long VitsBytes = 119326431L;
        const string VoskZipName = "vosk-model-small-cn-0.22.zip";
        const string VitsTarName = "vits-zh.tar.bz2";

        // 下载进度（已下载字节 / 需要下载的总字节）与当前步骤文案
        sealed class Job
        {
            public long Done;
            public long Total;
            public Action<long, long> Progress;
            public Func<bool> Cancelled;
            public Action<string> Stage;
        }

        // 模型要放到哪：环境变量 > 已经有模型的目录（和 VoiceOptions.DefaultModelsDir 一致）
        // > 程序目录（可写时，绿色版）> %LOCALAPPDATA%\Shiying\models（安装版在 Program Files 下写不进去）
        public static string TargetDir()
        {
            string env = Environment.GetEnvironmentVariable("SHIYING_MODELS");
            if (!string.IsNullOrEmpty(env))
            {
                return env;
            }

            string found = VoiceOptions.DefaultModelsDir();
            if (VoiceOptions.FindVoskModel(found) != null || HasVits(found))
            {
                return found;
            }

            string app = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
            if (CanWrite(app))
            {
                return app;
            }

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local))
            {
                return Path.Combine(local, "Shiying", "models");
            }

            return app;
        }

        static bool HasVits(string models)
        {
            string model, tokens, lexicon, dict;
            VoiceOptions.FindVits(models, out model, out tokens, out lexicon, out dict);
            return !string.IsNullOrEmpty(model) && !string.IsNullOrEmpty(tokens);
        }

        public static bool NeedVosk(string models)
        {
            return VoiceOptions.FindVoskModel(models) == null;
        }

        public static bool NeedVits(string models)
        {
            string model, tokens, lexicon, dict;
            VoiceOptions.FindVits(models, out model, out tokens, out lexicon, out dict);
            return string.IsNullOrEmpty(model) || string.IsNullOrEmpty(tokens);
        }

        // 需要下载的字节数，用来画进度条；已经有的模型不算进去
        public static long PendingBytes(string models)
        {
            long total = 0;
            if (NeedVosk(models))
            {
                total += VoskBytes;
            }
            if (NeedVits(models))
            {
                total += VitsBytes;
            }
            return total;
        }

        public static bool Ready(string models)
        {
            return !NeedVosk(models) && !NeedVits(models);
        }

        // 同步执行（调用方放后台线程）。失败抛异常，取消抛 OperationCanceledException。
        public static void Install(string models, Action<string> stage, Action<long, long> progress, Func<bool> cancelled)
        {
            Directory.CreateDirectory(models);
            bool vosk = NeedVosk(models);
            bool vits = NeedVits(models);
            if (!vosk && !vits)
            {
                if (stage != null)
                {
                    stage("模型已就绪");
                }
                return;
            }

            Job job = new Job();
            job.Stage = stage;
            job.Progress = progress;
            job.Cancelled = cancelled;
            job.Total = PendingBytes(models);

            string cache = Path.Combine(models, ".cache");
            Directory.CreateDirectory(cache);
            string tarExe = TarExe();

            if (vosk)
            {
                Stage(job, "下载语音识别模型 Vosk（43 MB）…");
                string zip = Path.Combine(cache, VoskZipName);
                Fetch(job, VoskUrl, zip, VoskBytes);

                Stage(job, "解压 Vosk 模型…");
                string voskRoot = Path.Combine(models, "vosk");
                Directory.CreateDirectory(voskRoot);
                ZipFile.ExtractToDirectory(zip, voskRoot, true);

                if (VoiceOptions.FindVoskModel(models) == null)
                {
                    throw new InvalidDataException("Vosk 压缩包里没有找到 am / conf 目录");
                }
                TryDelete(zip);
            }

            if (vits)
            {
                Stage(job, "下载朗读模型 VITS（114 MB）…");
                string tar = Path.Combine(cache, VitsTarName);
                Fetch(job, VitsUrl, tar, VitsBytes);

                Stage(job, "解压 VITS 模型…");
                string vitsRoot = Path.Combine(models, "vits");
                Directory.CreateDirectory(vitsRoot);
                ExtractTarBz2(tarExe, tar, vitsRoot);

                if (NeedVits(models))
                {
                    throw new InvalidDataException("VITS 压缩包里没有找到 *.onnx + tokens.txt");
                }
                TryDelete(tar);
            }

            TryDeleteDirectory(cache);
            Stage(job, "模型已就绪");
        }

        static void Stage(Job job, string text)
        {
            if (job.Stage != null)
            {
                job.Stage(text);
            }
        }

        static void Report(Job job)
        {
            if (job.Progress != null)
            {
                job.Progress(job.Done, job.Total);
            }
        }

        // 下载到 .cache，已有完整文件就跳过；下到一半的 .part 用 Range 续传
        static void Fetch(Job job, string url, string target, long expected)
        {
            long start = job.Done;
            FileInfo existing = new FileInfo(target);
            if (existing.Exists && existing.Length > 0)
            {
                job.Done = start + existing.Length;
                Report(job);
                return;
            }

            string part = target + ".part";
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (have > 0 && have >= expected)
            {
                TryDelete(part);
                have = 0;
            }

            // HttpWebRequest 是为了 Range 续传；SYSLIB0014 只是提示改用 HttpClient
#pragma warning disable SYSLIB0014
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
            request.UserAgent = "Shiying/" + AppInfo.Version;
            request.AllowAutoRedirect = true;
            request.Timeout = 60000;
            request.ReadWriteTimeout = 60000;
            if (have > 0)
            {
                request.AddRange(have);
            }

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                // 服务器不认 Range 就从头下，别把两段拼一起
                bool append = have > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                using (Stream input = response.GetResponseStream())
                using (var output = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    job.Done = start + output.Position;
                    Report(job);

                    byte[] buffer = new byte[81920];
                    while (true)
                    {
                        if (job.Cancelled != null && job.Cancelled())
                        {
                            throw new OperationCanceledException();
                        }

                        int read = input.Read(buffer, 0, buffer.Length);
                        if (read <= 0)
                        {
                            break;
                        }

                        output.Write(buffer, 0, read);
                        job.Done = start + output.Position;
                        Report(job);
                    }
                }
            }

            long size = new FileInfo(part).Length;
            if (expected > 0 && size != expected)
            {
                TryDelete(part);
                throw new InvalidDataException("下载不完整（" + size + " / " + expected + " 字节），请重试：" + url);
            }

            TryDelete(target);
            File.Move(part, target);
        }

        // Windows 10 1803+ 自带 bsdtar，可以直接解 bz2；没有就退回 PowerShell 的 tar
        static string TarExe()
        {
            string system = Path.Combine(Environment.SystemDirectory, "tar.exe");
            return File.Exists(system) ? system : "tar.exe";
        }

        static void ExtractTarBz2(string tarExe, string archive, string dest)
        {
            var info = new ProcessStartInfo();
            info.FileName = tarExe;
            info.Arguments = "-xjf \"" + archive + "\" -C \"" + dest + "\"";
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardError = true;
            info.RedirectStandardOutput = true;
            using (Process p = Process.Start(info))
            {
                string err = p.StandardError.ReadToEnd();
                p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    throw new InvalidDataException("解压失败（tar 退出码 " + p.ExitCode + "）：" + err.Trim());
                }
            }
        }

        static bool CanWrite(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write-test");
                using (File.Create(probe))
                {
                }
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0)
                {
                    Directory.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
