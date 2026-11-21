using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Shiying
{
    // latest.json 里的一条版本信息（由 scripts\make-latest.ps1 生成、setup.bat 随安装包一起推上服务器）
    sealed class UpdateInfo
    {
        public string Version = "";
        public string Url = "";
        public string Sha256 = "";
        public long Size;
        public string Notes = "";

        public string Display
        {
            get { return "v" + Version; }
        }

        public string SizeText
        {
            get { return Size > 0 ? (Size / 1048576.0).ToString("0.0") + " MB" : ""; }
        }
    }

    static class Updater
    {
        public const string FeedUrl = "http://111.230.247.111/shiying/latest.json";

        public static string DownloadFolder
        {
            get { return Path.Combine(Path.GetTempPath(), "ShiyingUpdate"); }
        }

        public static string InstallerPath
        {
            get { return Path.Combine(DownloadFolder, "ShiyingSetup.exe"); }
        }

        public static UpdateInfo Fetch()
        {
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "Shiying/" + AppInfo.Version;
                client.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                byte[] data = client.DownloadData(FeedUrl);
                return Parse(Encoding.UTF8.GetString(data));
            }
        }

        public static UpdateInfo Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new FormatException("latest.json 为空");
            }

            var info = new UpdateInfo();
            info.Version = Field(json, "version").Trim().TrimStart('v', 'V');
            info.Url = Field(json, "url");
            info.Sha256 = Field(json, "sha256");
            info.Notes = Field(json, "notes");

            Match size = Regex.Match(json, "\"size\"\\s*:\\s*(\\d+)");
            if (size.Success)
            {
                long.TryParse(size.Groups[1].Value, out info.Size);
            }

            if (info.Version.Length == 0 || info.Url.Length == 0)
            {
                throw new FormatException("latest.json 缺少 version 或 url");
            }

            return info;
        }

        static string Field(string json, string name)
        {
            Match match = Regex.Match(json, "\"" + name + "\"\\s*:\\s*\"([^\"]*)\"");
            if (!match.Success)
            {
                return "";
            }

            return match.Groups[1].Value.Replace("\\/", "/");
        }

        // 远端版本比本地新才更新；版本号按 主.次.修订 逐段比数字
        public static bool IsNewer(string remote, string local)
        {
            int[] r = Parts(remote);
            int[] l = Parts(local);
            for (int i = 0; i < 3; i++)
            {
                if (r[i] > l[i]) return true;
                if (r[i] < l[i]) return false;
            }

            return false;
        }

        static int[] Parts(string version)
        {
            var parts = new int[3];
            if (string.IsNullOrEmpty(version))
            {
                return parts;
            }

            string[] pieces = version.Split('.');
            for (int i = 0; i < 3 && i < pieces.Length; i++)
            {
                int value;
                if (int.TryParse(pieces[i].Trim(), out value))
                {
                    parts[i] = value;
                }
            }

            return parts;
        }

        // 下载到 %TEMP%\ShiyingUpdate，按 latest.json 的 sha256 校验后再换成正式文件名
        public static bool Download(UpdateInfo info, Action<long, long> progress, Func<bool> cancelled, out string error)
        {
            error = null;
            string part = InstallerPath + ".part";
            try
            {
                Directory.CreateDirectory(DownloadFolder);
                if (File.Exists(part))
                {
                    File.Delete(part);
                }

                using (var client = new WebClient())
                using (Stream input = client.OpenRead(info.Url))
                using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[65536];
                    long done = 0;
                    while (true)
                    {
                        if (cancelled != null && cancelled())
                        {
                            error = "已取消";
                            output.Dispose();
                            TryDelete(part);
                            return false;
                        }

                        int read = input.Read(buffer, 0, buffer.Length);
                        if (read <= 0)
                        {
                            break;
                        }

                        output.Write(buffer, 0, read);
                        done += read;
                        if (progress != null)
                        {
                            progress(done, info.Size);
                        }
                    }
                }

                if (info.Sha256.Length > 0)
                {
                    string actual = Sha256(part);
                    if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(part);
                        error = "安装包校验失败，sha256 不一致";
                        return false;
                    }
                }

                TryDelete(InstallerPath);
                File.Move(part, InstallerPath);
                return true;
            }
            catch (Exception ex)
            {
                TryDelete(part);
                error = ex.Message;
                return false;
            }
        }

        // /SILENT：只显示进度条，不弹任何页面，装完由安装包自己把新版本拉起来
        public static bool RunInstaller(out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(InstallerPath))
                {
                    error = "安装包不存在：" + InstallerPath;
                    return false;
                }

                var start = new ProcessStartInfo();
                start.FileName = InstallerPath;
                start.Arguments = "/SILENT /NORESTART /CLOSEAPPLICATIONS /NOCANCEL";
                start.UseShellExecute = true;
                Process.Start(start);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                var text = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    text.Append(hash[i].ToString("x2"));
                }

                return text.ToString();
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
    }
}
