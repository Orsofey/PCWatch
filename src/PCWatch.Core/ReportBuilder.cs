using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

namespace PCWatch.Core
{
    public static class ReportBuilder
    {
        public static string BuildHtml(IReadOnlyList<SystemSnapshot> history, IList<DiagnosticIssue> issues,
            AdvancedAnalysisResult advanced, string cpuName, string osName, string gpuNames)
        {
            SystemSnapshot latest = history != null && history.Count > 0 ? history[history.Count - 1] : new SystemSnapshot();
            StringBuilder b = new StringBuilder(16000);
            b.AppendLine("<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>PCWatch — отчёт</title>");
            b.AppendLine("<style>:root{color-scheme:dark}body{margin:0;background:#0c1017;color:#e9eef7;font:15px/1.6 'Segoe UI',sans-serif}main{max-width:1100px;margin:auto;padding:40px}h1{font-size:34px;margin:0}h2{margin-top:34px;color:#72d9ff}small,.muted{color:#94a3b8}.card{background:#151c27;border:1px solid #263247;border-radius:16px;padding:20px;margin:14px 0;box-shadow:0 12px 32px #0003}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:12px}.metric{font-size:24px;font-weight:600}table{width:100%;border-collapse:collapse}td,th{text-align:left;padding:10px;border-bottom:1px solid #283448}th{color:#94a3b8}.good{color:#54e0a2}.warning{color:#ffc66b}.critical{color:#ff7c91}.tag{font-size:12px;text-transform:uppercase;letter-spacing:.08em}code{color:#a9c7ff}@media print{body{background:white;color:#202530}.card{background:white;box-shadow:none}h2{color:#17648a}.muted,small,th{color:#596579}}</style></head><body><main>");
            b.AppendLine("<small>PCWATCH · SYSTEM PERFORMANCE REPORT</small><h1>Диагностика системы</h1>");
            b.AppendLine("<p class=\"muted\">Сформировано: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture) + "</p>");
            b.AppendLine("<div class=\"card\"><b>" + H(osName) + "</b><br><span class=\"muted\">" + H(cpuName) + "<br>" + H(gpuNames) + "</span></div>");
            b.AppendLine("<h2>Текущие показатели</h2><div class=\"grid\">");
            Metric(b, "Процессор", RussianFormat.Percent(latest.CpuUsage), RussianFormat.MegaHertz(latest.CpuClockMhz));
            Metric(b, "Память", RussianFormat.Percent(latest.MemoryUsage), RussianFormat.GigaBytes(latest.MemoryUsedGB) + " из " + RussianFormat.GigaBytes(latest.MemoryTotalGB));
            Metric(b, "Диск", RussianFormat.Percent(latest.DiskActive), RussianFormat.BytesPerSec(latest.DiskReadBytesPerSec + latest.DiskWriteBytesPerSec));
            Metric(b, "Видеокарта", RussianFormat.Percent(latest.GpuUsage), RussianFormat.GigaBytes(latest.GpuDedicatedUsedGB) + " выделенной");
            Metric(b, "Чтение страниц", RussianFormat.Rate(latest.PageReadsPerSec), "Файл подкачки: " + RussianFormat.Percent(latest.PageFileUsage));
            Metric(b, "Свободно на системном диске", RussianFormat.Percent(latest.SystemDriveFreePercent), RussianFormat.GigaBytes(latest.SystemDriveFreeGB));
            b.AppendLine("</div>");
            b.AppendLine("<h2>Заключение</h2>");
            if (issues != null)
                foreach (DiagnosticIssue issue in issues)
                {
                    string css = issue.Severity == IssueSeverity.Critical ? "critical" : issue.Severity == IssueSeverity.Warning ? "warning" : "good";
                    b.AppendLine("<div class=\"card\"><span class=\"tag " + css + "\">" + H(issue.Severity.ToString()) + "</span><h3>" + H(issue.Title) + "</h3><p>" + H(issue.Evidence) + "</p><p><b>Рекомендация:</b> " + H(issue.Recommendation) + "</p></div>");
                }
            b.AppendLine("<h2>Наиболее активные процессы</h2><div class=\"card\"><table><tr><th>Процесс</th><th>PID</th><th>ЦП</th><th>Память</th><th>Диск</th></tr>");
            List<ProcessSample> processes = new List<ProcessSample>(latest.Processes);
            processes.Sort(delegate(ProcessSample a, ProcessSample z) { return z.CpuUsage.CompareTo(a.CpuUsage); });
            for (int i = 0; i < Math.Min(10, processes.Count); i++)
                b.AppendLine("<tr><td>" + H(processes[i].Title) + " <small>" + H(processes[i].Name) + "</small></td><td>" + processes[i].Id + "</td><td>" + RussianFormat.Percent(processes[i].CpuUsage) + "</td><td>" + RussianFormat.Number(processes[i].MemoryMB) + " МБ</td><td>" + RussianFormat.BytesPerSec(processes[i].DiskBytesPerSec) + "</td></tr>");
            b.AppendLine("</table></div>");
            if (advanced != null)
            {
                b.AppendLine("<h2>ETW / DPC / ISR</h2><div class=\"card\"><p>" + H(advanced.Summary) + "</p><p class=\"muted\">Длительность: " + H(advanced.Duration.ToString()) + ". Максимум DPC: " + RussianFormat.Milliseconds(advanced.MaxDpcMs) + "; ISR: " + RussianFormat.Milliseconds(advanced.MaxIsrMs) + "; hard faults: " + advanced.HardPageFaults + ".</p>");
                if (!String.IsNullOrWhiteSpace(advanced.Warning)) b.AppendLine("<p class=\"warning\">" + H(advanced.Warning) + "</p>");
                b.AppendLine("<table><tr><th>Драйвер</th><th>DPC</th><th>ISR</th><th>Макс. DPC</th><th>Макс. ISR</th></tr>");
                foreach (DriverLatencySample d in advanced.Drivers)
                    b.AppendLine("<tr><td><code>" + H(d.Driver) + "</code></td><td>" + d.DpcCount + "</td><td>" + d.IsrCount + "</td><td>" + RussianFormat.Milliseconds(d.MaxDpcMs) + "</td><td>" + RussianFormat.Milliseconds(d.MaxIsrMs) + "</td></tr>");
                b.AppendLine("</table></div>");
            }
            b.AppendLine("<h2>Ограничения измерений</h2><div class=\"card muted\">Температура ЦП доступна только при наличии поддерживаемого системного датчика. Частота оценивается по счётчику Processor Performance относительно номинальной. DPC/ISR и hard page faults собираются ETW-сеансом администратора. Значения отзывчивости не являются универсальной оценкой общей производительности. PCWatch не изменяет систему автоматически.</div>");
            b.AppendLine("</main></body></html>");
            return b.ToString();
        }

        public static string BuildText(IReadOnlyList<SystemSnapshot> history, IList<DiagnosticIssue> issues,
            AdvancedAnalysisResult advanced, string cpuName, string osName, string gpuNames)
        {
            SystemSnapshot latest = history != null && history.Count > 0 ? history[history.Count - 1] : new SystemSnapshot();
            StringBuilder b = new StringBuilder(6000);
            b.AppendLine("PCWATCH — ОТЧЁТ О ПРОИЗВОДИТЕЛЬНОСТИ");
            b.AppendLine("Дата: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
            b.AppendLine(osName); b.AppendLine(cpuName); b.AppendLine(gpuNames); b.AppendLine();
            b.AppendLine("ЦП: " + RussianFormat.Percent(latest.CpuUsage) + ", " + RussianFormat.MegaHertz(latest.CpuClockMhz));
            b.AppendLine("ОЗУ: " + RussianFormat.Percent(latest.MemoryUsage) + ", " + RussianFormat.GigaBytes(latest.MemoryUsedGB) + " из " + RussianFormat.GigaBytes(latest.MemoryTotalGB));
            b.AppendLine("Диск: " + RussianFormat.Percent(latest.DiskActive) + ", задержка " + RussianFormat.Milliseconds(Math.Max(latest.DiskReadLatencyMs, latest.DiskWriteLatencyMs)));
            b.AppendLine("GPU: " + RussianFormat.Percent(latest.GpuUsage));
            b.AppendLine("Файл подкачки: " + RussianFormat.Percent(latest.PageFileUsage) + ", чтение страниц: " + RussianFormat.Rate(latest.PageReadsPerSec));
            b.AppendLine(); b.AppendLine("ЗАКЛЮЧЕНИЕ");
            if (issues != null)
                foreach (DiagnosticIssue issue in issues)
                {
                    b.AppendLine("[" + issue.Severity + "] " + issue.Title);
                    b.AppendLine(issue.Evidence); b.AppendLine("Рекомендация: " + issue.Recommendation); b.AppendLine();
                }
            if (advanced != null)
            {
                b.AppendLine("ETW: " + advanced.Summary);
                b.AppendLine("Макс. DPC: " + RussianFormat.Milliseconds(advanced.MaxDpcMs) + "; ISR: " + RussianFormat.Milliseconds(advanced.MaxIsrMs));
                foreach (DriverLatencySample d in advanced.Drivers)
                    b.AppendLine(d.Driver + ": DPC " + RussianFormat.Milliseconds(d.TotalDpcMs) + ", ISR " + RussianFormat.Milliseconds(d.TotalIsrMs));
            }
            b.AppendLine(); b.AppendLine("Температура и низкоуровневые метрики отображаются только при наличии поддерживаемого источника. PCWatch не изменяет систему автоматически.");
            return b.ToString();
        }

        private static void Metric(StringBuilder b, string title, string value, string subtitle)
        {
            b.AppendLine("<div class=\"card\"><small>" + H(title) + "</small><div class=\"metric\">" + H(value) + "</div><span class=\"muted\">" + H(subtitle) + "</span></div>");
        }

        private static string H(string value)
        {
            return WebUtility.HtmlEncode(value ?? "");
        }
    }
}
