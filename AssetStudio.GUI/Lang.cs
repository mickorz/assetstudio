using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AssetStudio.GUI
{
    /// <summary>
    /// 多语言管理器
    ///
    /// Lang 初始化流程:
    /// Program.Main()
    ///     -> Lang.LoadLanguage()
    ///            -> 从嵌入资源加载 JSON
    ///            -> 或从外部 Lang/ 目录覆盖
    ///     -> MainForm 构造函数
    ///            -> InitializeComponent()
    ///            -> ApplyTranslations()
    ///
    /// 语言切换:
    /// Options -> Language -> 选择语言
    ///     -> Lang.LoadLanguage()
    ///     -> ApplyTranslations()
    ///     -> 保存设置
    /// </summary>
    internal static class Lang
    {
        private static Dictionary<string, string> _strings = new Dictionary<string, string>();
        private static Dictionary<string, string> _displayNames = new Dictionary<string, string>();
        private static string[] _availableLanguages;

        public static string CurrentLanguage { get; private set; } = "zh-CN";

        public static string[] AvailableLanguages
        {
            get
            {
                if (_availableLanguages == null)
                {
                    var langs = new List<string>();
                    var assembly = Assembly.GetExecutingAssembly();
                    foreach (var name in assembly.GetManifestResourceNames())
                    {
                        const string prefix = "AssetStudio.GUI.Lang.";
                        if (name.StartsWith(prefix) && name.EndsWith(".json"))
                        {
                            var code = name.Substring(prefix.Length, name.Length - prefix.Length - 5);
                            langs.Add(code);
                        }
                    }
                    _availableLanguages = langs.ToArray();
                }
                return _availableLanguages;
            }
        }

        public static void LoadLanguage(string languageCode)
        {
            if (!AvailableLanguages.Contains(languageCode))
            {
                languageCode = "zh-CN";
            }

            CurrentLanguage = languageCode;
            var json = LoadJson(languageCode);
            if (json != null)
            {
                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                _strings.Clear();
                foreach (var kvp in data)
                {
                    if (kvp.Key == "_meta")
                    {
                        if (kvp.Value is JObject meta)
                        {
                            var dn = meta.Value<string>("displayName");
                            if (dn != null)
                                _displayNames[languageCode] = dn;
                        }
                        continue;
                    }
                    _strings[kvp.Key] = kvp.Value?.ToString() ?? kvp.Key;
                }
            }
        }

        public static string T(string key)
        {
            return _strings.TryGetValue(key, out var value) ? value : key;
        }

        public static string T(string key, params object[] args)
        {
            var format = T(key);
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        public static string GetDisplayName(string languageCode)
        {
            if (_displayNames.TryGetValue(languageCode, out var name))
                return name;
            return languageCode;
        }

        private static string LoadJson(string languageCode)
        {
            // 优先从外部文件加载，方便用户自定义
            var externalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lang", $"{languageCode}.json");
            if (File.Exists(externalPath))
            {
                return File.ReadAllText(externalPath);
            }

            // 从嵌入资源加载
            var resourceName = $"AssetStudio.GUI.Lang.{languageCode}.json";
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    using (var reader = new StreamReader(stream))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            return null;
        }
    }
}
