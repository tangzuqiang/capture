using System;
using System.Text;
#if NET
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
#endif

namespace Jietu
{
    sealed class LlmPreset
    {
        public string Name;
        public string BaseUrl;
        public string Model;
        public bool NeedsKey;
        public string Note;

        public LlmPreset(string name, string baseUrl, string model, bool needsKey, string note)
        {
            Name = name;
            BaseUrl = baseUrl;
            Model = model;
            NeedsKey = needsKey;
            Note = note;
        }

        public override string ToString()
        {
            return Name;
        }

        public static readonly LlmPreset[] All =
        {
            new LlmPreset("DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat", true, "platform.deepseek.com 创建 API Key"),
            new LlmPreset("通义千问", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus", true, "阿里云百炼控制台创建 API Key"),
            new LlmPreset("智谱 GLM", "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash", true, "glm-4-flash 免费，open.bigmodel.cn 创建 API Key"),
            new LlmPreset("Kimi", "https://api.moonshot.cn/v1", "moonshot-v1-8k", true, "platform.moonshot.cn 创建 API Key"),
            new LlmPreset("硅基流动", "https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-7B-Instruct", true, "部分小模型免费，cloud.siliconflow.cn 创建 API Key"),
            new LlmPreset("OpenAI", "https://api.openai.com/v1", "gpt-4o-mini", true, "platform.openai.com 创建 API Key"),
            new LlmPreset("Ollama 本地", "http://localhost:11434/v1", "qwen2.5:7b", false, "本机运行 Ollama，先执行 ollama pull qwen2.5:7b，不需要 Key"),
            new LlmPreset("自定义", "", "", false, "任意兼容 OpenAI /chat/completions 的接口")
        };

        public static LlmPreset Find(string name)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Name == name)
                {
                    return All[i];
                }
            }

            return null;
        }
    }

    sealed class TranslateTarget
    {
        public string Code;
        public string Label;
        public string Instruction;

        TranslateTarget(string code, string label, string instruction)
        {
            Code = code;
            Label = label;
            Instruction = instruction;
        }

        public override string ToString()
        {
            return Label;
        }

        public static readonly TranslateTarget[] All =
        {
            new TranslateTarget("auto", "自动（中文译英文，其他译中文）", "如果原文主要是中文，就翻译成英文；否则翻译成简体中文。"),
            new TranslateTarget("zh", "简体中文", "翻译成简体中文。"),
            new TranslateTarget("en", "English", "Translate into English."),
            new TranslateTarget("ja", "日本語", "翻译成日语。"),
            new TranslateTarget("ko", "한국어", "翻译成韩语。"),
            new TranslateTarget("zh-Hant", "繁體中文", "翻译成繁体中文。")
        };

        public static TranslateTarget Find(string code)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Code == code)
                {
                    return All[i];
                }
            }

            return All[0];
        }
    }

    sealed class LlmConfig
    {
        public string BaseUrl;
        public string ApiKey;
        public string Model;
        public string Target;

        public static LlmConfig FromSettings()
        {
            var config = new LlmConfig();
            config.BaseUrl = AppSettings.LlmBaseUrl;
            config.ApiKey = AppSettings.LlmApiKey;
            config.Model = AppSettings.LlmModel;
            config.Target = AppSettings.TranslateTarget;
            return config;
        }
    }

    static class LlmTranslator
    {
        public static string Translate(string text, LlmConfig config)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            if (config == null || string.IsNullOrWhiteSpace(config.BaseUrl) || string.IsNullOrWhiteSpace(config.Model))
            {
                throw new InvalidOperationException("还没有配置翻译模型。");
            }

#if NET
            string system = "你是专业的翻译引擎。" + TranslateTarget.Find(config.Target).Instruction
                + "只输出译文本身，保留原文的换行、编号和格式，不要解释，不要加引号或前后缀。"
                + "代码、网址、邮箱、数字和专有名词按惯例保留。";
            return Complete(config, system, text);
#else
            throw new NotSupportedException("翻译需要使用安装包里的程序。");
#endif
        }

#if NET
        static readonly HttpClient Http = CreateClient();

        static HttpClient CreateClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(120);
            return client;
        }

        static string Complete(LlmConfig config, string system, string user)
        {
            string url = config.BaseUrl.Trim().TrimEnd('/');
            if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                url += "/chat/completions";
            }

            var payload = new
            {
                model = config.Model.Trim(),
                temperature = 0.2,
                stream = false,
                messages = new object[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user }
                }
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey.Trim());
                }

                HttpResponseMessage response;
                try
                {
                    response = Task.Run(() => Http.SendAsync(request)).GetAwaiter().GetResult();
                }
                catch (TaskCanceledException)
                {
                    throw new TimeoutException("模型接口响应超时。");
                }
                catch (HttpRequestException ex)
                {
                    throw new InvalidOperationException("连不上模型接口：" + ex.Message, ex);
                }

                using (response)
                {
                    string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException("模型接口返回 " + (int)response.StatusCode + "：" + ErrorText(body));
                    }

                    return ReadAnswer(body);
                }
            }
        }

        static string ReadAnswer(string body)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement choices;
                    if (!doc.RootElement.TryGetProperty("choices", out choices) || choices.GetArrayLength() == 0)
                    {
                        throw new InvalidOperationException("模型没有返回内容：" + ErrorText(body));
                    }

                    JsonElement message = choices[0].GetProperty("message");
                    JsonElement content;
                    string text = message.TryGetProperty("content", out content) && content.ValueKind == JsonValueKind.String
                        ? content.GetString()
                        : "";
                    text = Regex.Replace(text ?? "", "<think>[\\s\\S]*?</think>", "").Trim();
                    return text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                }
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("模型返回的不是 JSON：" + Shorten(body));
            }
        }

        static string ErrorText(string body)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement error;
                    if (doc.RootElement.TryGetProperty("error", out error))
                    {
                        JsonElement message;
                        if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out message))
                        {
                            return message.ToString();
                        }

                        return error.ToString();
                    }
                }
            }
            catch (JsonException)
            {
            }

            return Shorten(body);
        }

        static string Shorten(string body)
        {
            body = (body ?? "").Trim();
            return body.Length > 300 ? body.Substring(0, 300) + "…" : body;
        }
#endif
    }
}
