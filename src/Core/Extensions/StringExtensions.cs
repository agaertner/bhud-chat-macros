﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Nekres.ChatMacros.Core {
    internal static class StringExtensions {
        public static string SplitCamelCase(this string input) {
            return Regex.Replace(input, "([A-Z])", " $1", RegexOptions.Compiled).Trim();
        }

        public static IEnumerable<string> Split(this string input, string delimiter) {
            return input.Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static string GetTextBetweenTags(this string input, string tagName) {
            var match = Regex.Match(input, $"<{tagName}>(.*?)</{tagName}>");
            return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value : string.Empty;
        }

        public static bool IsWebLink(this string uri) {
            return Uri.TryCreate(uri, UriKind.Absolute, out var uriResult)
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }
    }
}
