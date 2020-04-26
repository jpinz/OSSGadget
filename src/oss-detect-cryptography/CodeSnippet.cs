﻿using System.Collections.Generic;
using System.Security.Cryptography.Pkcs;
using Microsoft.DevSkim;
using Newtonsoft.Json;
using NLog.Fluent;

namespace Microsoft.CST.OpenSource.ML
{
    public class CodeSnippet
    {
        public int Version { get; set; } = 1;
        public string Identifier { get; set; } //This is the string at the end of the filename
        public CodeLanguage CodeLanguage { get; set; }
        public List<CodeAlgorithm> CodeAlgorithm { get; set; } = new List<CodeAlgorithm>();
        public string? PackageName { get; set; }
        public string? Src { get; set; }
        public string? Content { get; set; }
        public const string Separator = "----CODE_BEGINS----";

        public CodeSnippet(CodeLanguage CodeLanguage,string Identifier)
        {
            this.CodeLanguage = CodeLanguage;
            this.Identifier = Identifier;
        }

        public static CodeSnippet? FromString(string RawContent)
        {
            // Split by separator
            var splits = RawContent.Split(Separator);
            if (splits.Length > 1)
            {
                var CodeSnippet = JsonConvert.DeserializeObject<CodeSnippet>(splits[0]);
                // If somehow the code contained a seaprator put it back together and then save into content.
                CodeSnippet.Content = string.Join(Separator, splits[1..]);
                return CodeSnippet;
            }
            else
            {
                Log.Debug("Couldn't find a separator.");
                return null;
            }
        }

        public override string ToString()
        {
            return $"{JsonConvert.SerializeObject(this)}{Separator}{Content}";
        }
    }
}
