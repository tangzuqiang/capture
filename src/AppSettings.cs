using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Shiying
{
    enum ScrollPace
    {
        Slow,
        Normal,
        Fast
    }

    static class AppSettings
    {
        public static ScrollPace Pace = ScrollPace.Normal;
        public static Keys Gesture = Keys.Control | Keys.Alt | Keys.A;
        public static Keys GestureRgb = Keys.Control | Keys.Alt | Keys.C;
        public static Keys GestureWeb = Keys.Control | Keys.Alt | Keys.X;
        public static Keys GestureRecord = Keys.Control | Keys.Alt | Keys.R;
        public static bool RecordSystemAudio = true;
        public static bool RecordMicrophone = false;
        public static string SaveFolder = "";
        public static string LlmPreset = "";
        public static string LlmBaseUrl = "";
        public static string LlmApiKey = "";
        public static string LlmModel = "";
        public static string TranslateTarget = "auto";
        public static bool AutoCheckUpdate = true;
        public static string SkipVersion = "";
        public static DateTime LastUpdateCheck = DateTime.MinValue;
        public const string DefaultUpdateServer = "192.168.11.10:8001";
        public static string UpdateServer = DefaultUpdateServer;

        public static bool LlmConfigured
        {
            get { return LlmBaseUrl.Length > 0 && LlmModel.Length > 0; }
        }

        public static string DefaultPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Shiying");
                return Path.Combine(dir, "settings.txt");
            }
        }

        public static int SettleMilliseconds
        {
            get
            {
                if (Pace == ScrollPace.Slow) return 320;
                if (Pace == ScrollPace.Fast) return 140;
                return 220;
            }
        }

        public static int StableMilliseconds
        {
            get
            {
                if (Pace == ScrollPace.Slow) return 560;
                if (Pace == ScrollPace.Fast) return 220;
                return 380;
            }
        }

        public static double TargetRatio
        {
            get
            {
                if (Pace == ScrollPace.Slow) return 0.32;
                if (Pace == ScrollPace.Fast) return 0.52;
                return 0.42;
            }
        }

        public static void Load()
        {
            MigrateLegacySettings();
            Load(DefaultPath);
        }

        // 旧版品牌名是 Jietu，配置放在 %AppData%\Jietu。改名成 Shiying 后老用户的
        // 快捷键、保存目录、模型配置不能丢：新位置没有、旧位置有时复制一份过去。
        // 旧目录保留不动，万一要回退还不至于丢配置。
        static void MigrateLegacySettings()
        {
            try
            {
                string legacy = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Jietu");
                string legacyFile = Path.Combine(legacy, "settings.txt");
                if (File.Exists(DefaultPath) || !File.Exists(legacyFile))
                {
                    return;
                }

                string dir = Path.GetDirectoryName(DefaultPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.Copy(legacyFile, DefaultPath, true);
            }
            catch (Exception)
            {
                // 迁移失败不该挡住启动，读不到就当默认配置
            }
        }

        public static void Load(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return;
                }

                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    if (key == "pace")
                    {
                        ScrollPace pace;
                        if (Enum.TryParse(value, out pace))
                        {
                            Pace = pace;
                        }
                    }
                    else if (key == "saveFolder")
                    {
                        SaveFolder = value;
                    }
                    else if (key == "recordSystemAudio" || key == "recordMicrophone")
                    {
                        bool on;
                        if (bool.TryParse(value, out on))
                        {
                            if (key == "recordMicrophone") RecordMicrophone = on;
                            else RecordSystemAudio = on;
                        }
                    }
                    else if (key == "llmPreset")
                    {
                        LlmPreset = value;
                    }
                    else if (key == "llmBaseUrl")
                    {
                        LlmBaseUrl = value;
                    }
                    else if (key == "llmModel")
                    {
                        LlmModel = value;
                    }
                    else if (key == "llmApiKey")
                    {
                        LlmApiKey = Unprotect(value);
                    }
                    else if (key == "translateTarget")
                    {
                        TranslateTarget = value.Length > 0 ? value : "auto";
                    }
                    else if (key == "autoCheckUpdate")
                    {
                        bool on;
                        if (bool.TryParse(value, out on))
                        {
                            AutoCheckUpdate = on;
                        }
                    }
                    else if (key == "skipVersion")
                    {
                        SkipVersion = value;
                    }
                    else if (key == "lastUpdateCheck")
                    {
                        long ticks;
                        if (long.TryParse(value, out ticks) && ticks > 0)
                        {
                            LastUpdateCheck = new DateTime(ticks, DateTimeKind.Utc);
                        }
                    }
                    else if (key == "updateServer")
                    {
                        UpdateServer = NormalizeUpdateServer(value);
                    }
                    else if (key == "gesture" || key == "gestureRgb" || key == "gestureWeb" || key == "gestureRecord")
                    {
                        Keys gesture;
                        if (Enum.TryParse(value, out gesture) && IsValidGesture(gesture))
                        {
                            gesture = Normalize(gesture);
                            if (key == "gestureRgb") GestureRgb = gesture;
                            else if (key == "gestureWeb") GestureWeb = gesture;
                            else if (key == "gestureRecord") GestureRecord = gesture;
                            else Gesture = gesture;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public static void Save()
        {
            Save(DefaultPath);
        }

        public static void Save(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string body = "pace=" + Pace
                    + "\r\ngesture=" + Gesture
                    + "\r\ngestureRgb=" + GestureRgb
                    + "\r\ngestureWeb=" + GestureWeb
                    + "\r\ngestureRecord=" + GestureRecord
                    + "\r\nrecordSystemAudio=" + RecordSystemAudio
                    + "\r\nrecordMicrophone=" + RecordMicrophone
                    + "\r\nsaveFolder=" + SaveFolder
                    + "\r\nllmPreset=" + OneLine(LlmPreset)
                    + "\r\nllmBaseUrl=" + OneLine(LlmBaseUrl)
                    + "\r\nllmModel=" + OneLine(LlmModel)
                    + "\r\nllmApiKey=" + Protect(OneLine(LlmApiKey))
                    + "\r\ntranslateTarget=" + OneLine(TranslateTarget)
                    + "\r\nautoCheckUpdate=" + AutoCheckUpdate
                    + "\r\nskipVersion=" + OneLine(SkipVersion)
                    + "\r\nlastUpdateCheck=" + (LastUpdateCheck == DateTime.MinValue ? "0" : LastUpdateCheck.Ticks.ToString())
                    + "\r\nupdateServer=" + NormalizeUpdateServer(UpdateServer)
                    + "\r\n";
                string temp = path + ".tmp";
                File.WriteAllText(temp, body, Encoding.UTF8);
                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch
            {
            }
        }

        static string OneLine(string value)
        {
            return (value ?? "").Replace("\r", "").Replace("\n", "").Trim();
        }

        public static string NormalizeUpdateServer(string value)
        {
            return string.Equals(value, "111.230.247.111", StringComparison.Ordinal)
                ? "111.230.247.111"
                : DefaultUpdateServer;
        }

        static string Protect(string secret)
        {
            if (string.IsNullOrEmpty(secret))
            {
                return "";
            }

            byte[] sealedBytes = NativeMethods.ProtectBytes(Encoding.UTF8.GetBytes(secret));
            return sealedBytes == null ? "" : "dpapi:" + Convert.ToBase64String(sealedBytes);
        }

        static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored) || !stored.StartsWith("dpapi:", StringComparison.Ordinal))
            {
                return "";
            }

            try
            {
                byte[] plain = NativeMethods.UnprotectBytes(Convert.FromBase64String(stored.Substring(6)));
                return plain == null ? "" : Encoding.UTF8.GetString(plain);
            }
            catch (FormatException)
            {
                return "";
            }
        }

        public static string SaveDirectory()
        {
            if (!string.IsNullOrEmpty(SaveFolder) && Directory.Exists(SaveFolder))
            {
                return SaveFolder;
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        }

        public static string VideoDirectory()
        {
            if (!string.IsNullOrEmpty(SaveFolder) && Directory.Exists(SaveFolder))
            {
                return SaveFolder;
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        }

        public static bool IsValidGesture(Keys gesture)
        {
            Keys key = gesture & Keys.KeyCode;
            Keys mods = gesture & (Keys.Control | Keys.Alt | Keys.Shift);
            if (mods == Keys.None)
            {
                return false;
            }

            if (key == Keys.None || key == Keys.ControlKey || key == Keys.Menu || key == Keys.ShiftKey || key == Keys.LWin || key == Keys.RWin)
            {
                return false;
            }

            return true;
        }

        public static Keys Normalize(Keys gesture)
        {
            return (gesture & (Keys.Control | Keys.Alt | Keys.Shift)) | (gesture & Keys.KeyCode);
        }

        public static uint NativeModifiers(Keys gesture)
        {
            uint mods = NativeMethods.MOD_NOREPEAT;
            if ((gesture & Keys.Control) != 0)
            {
                mods |= NativeMethods.MOD_CONTROL;
            }

            if ((gesture & Keys.Alt) != 0)
            {
                mods |= NativeMethods.MOD_ALT;
            }

            if ((gesture & Keys.Shift) != 0)
            {
                mods |= NativeMethods.MOD_SHIFT;
            }

            return mods;
        }

        public static string FormatGesture(Keys gesture)
        {
            string text = "";
            if ((gesture & Keys.Control) != 0)
            {
                text = Join(text, "Ctrl");
            }

            if ((gesture & Keys.Alt) != 0)
            {
                text = Join(text, "Alt");
            }

            if ((gesture & Keys.Shift) != 0)
            {
                text = Join(text, "Shift");
            }

            return Join(text, FormatKey(gesture & Keys.KeyCode));
        }

        static string Join(string left, string right)
        {
            if (left.Length == 0)
            {
                return right;
            }

            return left + " + " + right;
        }

        static string FormatKey(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9)
            {
                return ((int)(key - Keys.D0)).ToString();
            }

            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
            {
                return "Num" + (int)(key - Keys.NumPad0);
            }

            switch (key)
            {
                case Keys.Space: return "Space";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.Oemtilde: return "`";
                default: return key.ToString();
            }
        }
    }
}
