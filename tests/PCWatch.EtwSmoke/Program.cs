using System;
using System.IO;
using System.Text;
using PCWatch.Core;
using PCWatch.Services;

namespace PCWatch.EtwSmoke
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            int seconds = 10;
            string outputPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                int parsed;
                if (Int32.TryParse(args[i], out parsed)) seconds = parsed;
                else if (args[i].StartsWith("--output=", StringComparison.OrdinalIgnoreCase)) outputPath = args[i].Substring(9).Trim('"');
            }
            StringBuilder log = new StringBuilder();
            int exitCode = 0;
            try
            {
                EtwAnalysisService service = new EtwAnalysisService();
                if (!service.IsElevated)
                {
                    Line(log, "Administrator rights are required.");
                    exitCode = 2;
                }
                else
                {
                    AdvancedAnalysisResult result = service.AnalyzeAsync(seconds, null).Result;
                    Line(log, result.Summary);
                    Line(log, String.Format("Duration: {0:0.0}s; DPC: {1:0.000}ms; ISR: {2:0.000}ms; dispatch: {3:0.000}ms; hard faults: {4}; lost: {5}", result.Duration.TotalSeconds, result.MaxDpcMs, result.MaxIsrMs, result.MaxDispatchLatencyMs, result.HardPageFaults, result.LostEvents));
                    Line(log, "Drivers: " + result.Drivers.Count);
                    for (int i = 0; i < Math.Min(10, result.Drivers.Count); i++)
                        Line(log, String.Format("{0}: total {1:0.000}ms; DPC max {2:0.000}ms; ISR max {3:0.000}ms", result.Drivers[i].Driver, result.Drivers[i].TotalMs, result.Drivers[i].MaxDpcMs, result.Drivers[i].MaxIsrMs));
                    if (!String.IsNullOrWhiteSpace(result.Warning)) Line(log, "Warning: " + result.Warning);
                    exitCode = result.Duration.TotalSeconds >= seconds - 2 ? 0 : 1;
                }
            }
            catch (Exception ex)
            {
                Line(log, ex.ToString());
                exitCode = 1;
            }
            finally
            {
                if (!String.IsNullOrWhiteSpace(outputPath))
                {
                    try { File.WriteAllText(outputPath, log.ToString(), new UTF8Encoding(true)); }
                    catch (Exception ex) { Console.Error.WriteLine("Output error: " + ex.Message); }
                }
            }
            return exitCode;
        }

        private static void Line(StringBuilder log, string text)
        {
            log.AppendLine(text);
            Console.WriteLine(text);
        }
    }
}
