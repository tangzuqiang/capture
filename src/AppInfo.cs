using System;
using System.IO;
using System.Windows.Forms;

namespace Shiying
{
    // 版本号来源：优先读程序目录的 version.txt（构建脚本写入，安装包和绿色版都带），
    // 读不到就退回程序集里的 ProductVersion（dotnet publish 会把 csproj 的 Version 编进去）。
    static class AppInfo
    {
        public static string Version
        {
            get
            {
                try
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
                    if (File.Exists(path))
                    {
                        string text = File.ReadAllText(path).Trim();
                        if (text.Length > 0)
                        {
                            return text;
                        }
                    }
                }
                catch (Exception)
                {
                    // 读版本文件失败不影响使用，继续走程序集版本
                }

                string raw = Application.ProductVersion ?? "";
                int plus = raw.IndexOf('+');
                if (plus > 0)
                {
                    raw = raw.Substring(0, plus);
                }

                raw = raw.Trim();
                if (raw.Length == 0 || raw == "0.0.0" || raw == "0.0.0.0")
                {
                    return "";
                }

                return raw;
            }
        }

        public static string Title
        {
            get
            {
                string version = Version;
                return version.Length == 0 ? "拾影" : "拾影 v" + version;
            }
        }
    }
}
