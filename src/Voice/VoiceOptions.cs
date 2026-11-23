using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Shiying.Voice
{
    // 一条"说法 -> 意图"的映射
    class VoicePhrase
    {
        public string Intent;
        public string Phrase;
    }

    // 语音助手运行参数：模型在哪儿、命令表长什么样。
    // 模型不进安装包，运行时从 <程序目录>\models 下读取，可用 models\voice\voice.json 覆盖自动探测结果。
    class VoiceOptions
    {
        // 可选：Vosk 模型目录（内含 am / conf / graph）
        public string VoskModelDir;
        // 可选：VITS 的 onnx 模型、tokens、lexicon、dict（中文模型通常四个都要）
        public string VitsModel;
        public string VitsTokens;
        public string VitsLexicon;
        public string VitsDictDir;
        // 说话人编号与语速
        public int SpeakerId = 0;
        public float Speed = 1.0f;
        // 播报期间暂时不喂音频，避免自己的喇叭声被识别成命令（要做"边说边打断"就设 false）
        public bool MuteWhileSpeaking = true;
        // 识别置信度下限，低于它的结果不再向上抛
        public double MinConfidence = 0.65;

        public string ModelsDir;
        public List<VoicePhrase> Phrases = new List<VoicePhrase>();

        // 模型目录查找顺序：
        //   1) 环境变量 SHIYING_MODELS（指向 models 目录）
        //   2) 程序目录\models（绿色版/自行拷贝）
        //   3) %LOCALAPPDATA%\Shiying\models（安装版，Program Files 下不好塞大文件）
        //   4) %APPDATA%\Shiying\models
        // 谁先有 vosk 或 vits 子目录就用谁，都没有则回落到程序目录\models（用来报"缺模型"）
        public static string DefaultModelsDir()
        {
            string env = Environment.GetEnvironmentVariable("SHIYING_MODELS");
            if (!string.IsNullOrEmpty(env))
            {
                return env;
            }

            List<string> candidates = new List<string>();
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models"));

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local))
            {
                candidates.Add(Path.Combine(local, "Shiying", "models"));
            }

            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(roaming))
            {
                candidates.Add(Path.Combine(roaming, "Shiying", "models"));
            }

            foreach (string c in candidates)
            {
                if (Directory.Exists(Path.Combine(c, "vosk")) || Directory.Exists(Path.Combine(c, "vits")))
                {
                    return c;
                }
            }

            return candidates[0];
        }

        // 读取 models\voice\voice.json + models\voice\commands.json，没配的项走自动探测
        public static VoiceOptions Load(string modelsDir)
        {
            if (string.IsNullOrEmpty(modelsDir))
            {
                modelsDir = DefaultModelsDir();
            }

            VoiceOptions opt = new VoiceOptions();
            opt.ModelsDir = modelsDir;

            string voiceDir = Path.Combine(modelsDir, "voice");
            string confPath = Path.Combine(voiceDir, "voice.json");
            if (File.Exists(confPath))
            {
                try
                {
                    opt.ApplyJson(File.ReadAllText(confPath));
                }
                catch (Exception)
                {
                    // 配置文件写坏了不影响启动，后面按自动探测走
                }
            }

            if (string.IsNullOrEmpty(opt.VoskModelDir))
            {
                opt.VoskModelDir = VoiceOptions.FindVoskModel(modelsDir);
            }

            if (string.IsNullOrEmpty(opt.VitsModel))
            {
                VoiceOptions.FindVits(modelDir: modelsDir,
                    model: out string model, tokens: out string tokens,
                    lexicon: out string lexicon, dictDir: out string dictDir);
                opt.VitsModel = model;
                if (string.IsNullOrEmpty(opt.VitsTokens))
                {
                    opt.VitsTokens = tokens;
                }
                if (string.IsNullOrEmpty(opt.VitsLexicon))
                {
                    opt.VitsLexicon = lexicon;
                }
                if (string.IsNullOrEmpty(opt.VitsDictDir))
                {
                    opt.VitsDictDir = dictDir;
                }
            }

            opt.LoadPhrases(Path.Combine(voiceDir, "commands.json"));
            if (opt.Phrases.Count == 0)
            {
                opt.Phrases = VoiceOptions.DefaultPhrases();
            }

            return opt;
        }

        void ApplyJson(string json)
        {
            JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            VoskModelDir = ReadString(root, "voskModelDir", VoskModelDir);
            VitsModel = ReadString(root, "vitsModel", VitsModel);
            VitsTokens = ReadString(root, "vitsTokens", VitsTokens);
            VitsLexicon = ReadString(root, "vitsLexicon", VitsLexicon);
            VitsDictDir = ReadString(root, "vitsDictDir", VitsDictDir);

            if (root.TryGetProperty("speakerId", out JsonElement sid) && sid.ValueKind == JsonValueKind.Number)
            {
                SpeakerId = sid.GetInt32();
            }
            if (root.TryGetProperty("speed", out JsonElement sp) && sp.ValueKind == JsonValueKind.Number)
            {
                Speed = (float)sp.GetDouble();
            }
            if (root.TryGetProperty("muteWhileSpeaking", out JsonElement ms))
            {
                MuteWhileSpeaking = ReadBool(ms, MuteWhileSpeaking);
            }
            if (root.TryGetProperty("minConfidence", out JsonElement mc) && mc.ValueKind == JsonValueKind.Number)
            {
                MinConfidence = mc.GetDouble();
            }
        }

        static bool ReadBool(JsonElement el, bool fallback)
        {
            if (el.ValueKind == JsonValueKind.True)
            {
                return true;
            }
            if (el.ValueKind == JsonValueKind.False)
            {
                return false;
            }
            return fallback;
        }

        static string ReadString(JsonElement root, string name, string fallback)
        {
            if (root.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String)
            {
                string v = el.GetString();
                if (!string.IsNullOrEmpty(v))
                {
                    return v;
                }
            }
            return fallback;
        }

        void LoadPhrases(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                List<VoicePhrase> list = new List<VoicePhrase>();
                foreach (JsonElement item in root.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    if (!item.TryGetProperty("intent", out JsonElement it) || it.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }
                    string intent = it.GetString();
                    if (!item.TryGetProperty("phrases", out JsonElement ph) || ph.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }
                    foreach (JsonElement p in ph.EnumerateArray())
                    {
                        if (p.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }
                        string phrase = p.GetString();
                        if (string.IsNullOrEmpty(phrase))
                        {
                            continue;
                        }
                        VoicePhrase vp = new VoicePhrase();
                        vp.Intent = intent;
                        vp.Phrase = phrase.Trim();
                        list.Add(vp);
                    }
                }
                Phrases = list;
            }
            catch (Exception)
            {
                // 命令表格式不对就退回内置默认
            }
        }

        // 找不到命令表时的兜底说法（可按需改写 models\voice\commands.json）
        public static List<VoicePhrase> DefaultPhrases()
        {
            List<VoicePhrase> list = new List<VoicePhrase>();
            AddPhrases(list, "capture", new string[] { "截图", "截个图", "抓屏", "屏幕截图" });
            AddPhrases(list, "record_start", new string[] { "开始录制", "开始录像", "开始录屏" });
            AddPhrases(list, "record_stop", new string[] { "停止录制", "结束录制", "停止录屏" });
            AddPhrases(list, "ocr", new string[] { "文字识别", "识别文字", "提取文字" });
            AddPhrases(list, "save", new string[] { "保存", "保存一下" });
            AddPhrases(list, "cancel", new string[] { "取消", "退出", "关闭" });
            AddPhrases(list, "stop_talking", new string[] { "别说了", "安静", "停止播报" });
            return list;
        }

        static void AddPhrases(List<VoicePhrase> list, string intent, string[] phrases)
        {
            foreach (string p in phrases)
            {
                VoicePhrase vp = new VoicePhrase();
                vp.Intent = intent;
                vp.Phrase = p;
                list.Add(vp);
            }
        }

        // 词表模式用的说法清单（去重）
        public string[] GrammarPhrases()
        {
            List<string> list = new List<string>();
            foreach (VoicePhrase p in Phrases)
            {
                if (!string.IsNullOrEmpty(p.Phrase) && !list.Contains(p.Phrase))
                {
                    list.Add(p.Phrase);
                }
            }
            return list.ToArray();
        }

        public bool HasVosk
        {
            get { return !string.IsNullOrEmpty(VoskModelDir); }
        }

        public bool HasVits
        {
            get { return !string.IsNullOrEmpty(VitsModel) && !string.IsNullOrEmpty(VitsTokens); }
        }

        // 缺哪些文件，直接给用户看
        public string MissingHint()
        {
            List<string> miss = new List<string>();
            if (!HasVosk)
            {
                miss.Add("models\\vosk\\<模型目录>（内含 am、conf、graph 等）");
            }
            if (!HasVits)
            {
                miss.Add("models\\vits\\model.onnx + tokens.txt（中文模型一般还要 lexicon.txt 和 dict）");
            }
            if (miss.Count == 0)
            {
                return "";
            }
            return "缺少语音模型：" + string.Join("；", miss.ToArray())
                + "。可以右键托盘图标点「下载语音模型…」，或执行 scripts\\fetch-voice-models.ps1 -ModelsDir \""
                + ModelsDir + "\" 下载（约 160 MB，只需一次）。";
        }

        // 在 models\vosk 下找第一个同时有 am 和 conf 的目录
        public static string FindVoskModel(string modelsDir)
        {
            try
            {
                string voskRoot = Path.Combine(modelsDir, "vosk");
                if (!Directory.Exists(voskRoot))
                {
                    return null;
                }
                foreach (string dir in Directory.GetDirectories(voskRoot).OrderBy(x => x))
                {
                    if (Directory.Exists(Path.Combine(dir, "am")) && Directory.Exists(Path.Combine(dir, "conf")))
                    {
                        return dir;
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        // 在 models\vits 下找 onnx + tokens.txt（lexicon / dict 有就一起带上）
        public static void FindVits(string modelDir, out string model, out string tokens,
            out string lexicon, out string dictDir)
        {
            model = null;
            tokens = null;
            lexicon = null;
            dictDir = null;

            try
            {
                string vitsRoot = Path.Combine(modelDir, "vits");
                if (!Directory.Exists(vitsRoot))
                {
                    return;
                }

                string[] onnx = Directory.GetFiles(vitsRoot, "*.onnx", SearchOption.AllDirectories)
                    .OrderBy(x => x.Length)
                    .ToArray();
                foreach (string path in onnx)
                {
                    string dir = Path.GetDirectoryName(path);
                    string tok = Path.Combine(dir, "tokens.txt");
                    if (!File.Exists(tok))
                    {
                        continue;
                    }
                    model = path;
                    tokens = tok;

                    string lex = Path.Combine(dir, "lexicon.txt");
                    lexicon = File.Exists(lex) ? lex : null;

                    string dict = Path.Combine(dir, "dict");
                    dictDir = Directory.Exists(dict) ? dict : null;
                    return;
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
