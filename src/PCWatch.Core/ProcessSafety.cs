using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PCWatch.Core
{
    /// <summary>
    /// Защита критичных для Windows процессов от завершения.
    /// </summary>
    public static class ProcessSafety
    {
        private static readonly HashSet<string> CriticalNames = CreateCriticalNames();

        private static HashSet<string> CreateCriticalNames()
        {
            string[] names = new string[]
            {
                "system",
                "idle",
                "system idle process",
                "secure system",
                "registry",
                "memory compression",
                "csrss",
                "smss",
                "wininit",
                "winlogon",
                "services",
                "lsass",
                "lsaiso",
                "lsm",
                "svchost",
                "dwm",
                "fontdrvhost",
                "sihost",
                "taskhostw",
                "msmpeng",
                "nissrv",
                "securityhealthservice",
                "windefend",
                "wmiprvse",
                "conhost",
                "spoolsv",
                "winlogon.exe",
                "pcwatch"
            };
            return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Возвращает true, если процесс разрешено завершать, и пустую причину.
        /// Иначе false с объяснением на русском языке.
        /// </summary>
        public static bool CanTerminate(ProcessSample sample, int currentProcessId, out string reason)
        {
            reason = "";
            if (sample == null)
            {
                reason = "Процесс не найден.";
                return false;
            }

            string name = NormalizeName(sample.Name);
            if (sample.Id == 0)
            {
                reason = "Нельзя завершать системный процесс с PID 0 (System Idle).";
                return false;
            }
            if (sample.Id == 4)
            {
                reason = "Нельзя завершать процесс с PID 4 (ядро Windows).";
                return false;
            }
            if (sample.Id == currentProcessId)
            {
                reason = "Нельзя завершать сам PCWatch.";
                return false;
            }
            if (name.Length > 0 && CriticalNames.Contains(name))
            {
                reason = "Критичный для работы Windows процесс (" + sample.Name + ") нельзя завершать.";
                return false;
            }

            return true;
        }

        public static bool IsCurrentIdentity(Process process, ProcessSample expected)
        {
            if (process == null || expected == null) return false;
            try
            {
                if (process.Id != expected.Id) return false;
                if (!String.Equals(NormalizeName(process.ProcessName), NormalizeName(expected.Name), StringComparison.OrdinalIgnoreCase)) return false;
                return expected.StartTime == DateTime.MinValue || process.StartTime == expected.StartTime;
            }
            catch { return false; }
        }

        private static string NormalizeName(string name)
        {
            string value = name == null ? "" : name.Trim();
            if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 4);
            }
            return value;
        }
    }
}
