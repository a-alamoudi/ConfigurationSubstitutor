﻿using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ConfigurationSubstitution
{
    public class ConfigurationSubstitutor
    {
        // A shared thread static to avoid allocation on each request.
        [ThreadStatic]
        private static HashSet<string> _recursionDetectionSet;

        private readonly string _startsWith;
        private readonly string _endsWith;
        private readonly string _fallbackDefaultValueDelimiter;
        private Regex _findSubstitutions;
        private readonly bool _exceptionOnMissingVariables;

        public ConfigurationSubstitutor(bool exceptionOnMissingVariables = true) : this("{", "}", exceptionOnMissingVariables)
        {
        }

        public ConfigurationSubstitutor(string substitutableStartsWith, string substitutableEndsWith, bool exceptionOnMissingVariables = true, string fallbackDefaultValueDelimiter = "")
        {
            _startsWith = !String.IsNullOrEmpty(substitutableStartsWith) ? substitutableStartsWith : throw new ArgumentException(
                $"Invalid substitutableStartsWith value", nameof(substitutableStartsWith));
            _endsWith = !String.IsNullOrEmpty(substitutableEndsWith) ? substitutableEndsWith : throw new ArgumentException(
                $"Invalid substitutableEndsWith value", nameof(substitutableEndsWith));
            _fallbackDefaultValueDelimiter = fallbackDefaultValueDelimiter ?? throw new ArgumentNullException(
                nameof(fallbackDefaultValueDelimiter), $"Invalid fallbackDefaultValueDelimiter value");
            var escapedStart = Regex.Escape(_startsWith);
            var escapedEnd = Regex.Escape(_endsWith);

            _findSubstitutions = new Regex("(?<=" + escapedStart + ")(.*?)(?=" + escapedEnd + ")",
                RegexOptions.Compiled);
            _exceptionOnMissingVariables = exceptionOnMissingVariables;
        }

        public string GetSubstituted(IConfiguration configuration, string key)
        {
            if (_recursionDetectionSet == null)
            {
                _recursionDetectionSet = new HashSet<string>();
            }

            _recursionDetectionSet.Clear();
            return GetSubstituted(configuration, key, _recursionDetectionSet);
        }

        private string GetSubstituted(IConfiguration configuration, string key, HashSet<string> recursionDetectionSet)
        {
            var value = configuration[key];
            if (value == null) return value;

            return ApplySubstitution(configuration, value, recursionDetectionSet);
        }

        private string ApplySubstitution(IConfiguration configuration, string value, HashSet<string> recursionDetectionSet)
        {
            if (!recursionDetectionSet.Add(value))
            {
                throw new EndlessRecursionVariableException(value);
            }

            var captures = _findSubstitutions.Matches(value).Cast<Match>().SelectMany(m => m.Captures.Cast<Capture>());
            foreach (var capture in captures)
            {
                var substitutedValue = this.GetSubstituted(configuration, capture.Value, recursionDetectionSet);

                if (substitutedValue == null && !string.IsNullOrEmpty(_fallbackDefaultValueDelimiter))
                {
                    var delimitedVals = capture.Value.Split(new[] { _fallbackDefaultValueDelimiter }, 2, StringSplitOptions.None);
                    // in case delimiter matches
                    if (delimitedVals.Length == 2)
                    {
                        value = value.Replace(capture.Value, delimitedVals[0]);
                        // if declared EV isn't resolvable, assign it to provided fallback default value
                        if (String.IsNullOrEmpty(configuration[delimitedVals[0]]))
                        {
                            configuration[delimitedVals[0]] = delimitedVals[1];
                        }

                        return ApplySubstitution(configuration, value, recursionDetectionSet);
                    }

                }
                if (substitutedValue == null && _exceptionOnMissingVariables)
                {
                    throw new UndefinedConfigVariableException($"{_startsWith}{capture.Value}{_endsWith}");
                }
                value = value.Replace(_startsWith + capture.Value + _endsWith, substitutedValue);
            }

            recursionDetectionSet.Remove(value);

            return value;
        }
    }
}
