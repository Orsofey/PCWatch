using System;
using System.Collections.Generic;

namespace PCWatch.Core
{
    /// <summary>
    /// Оценивает правила AppSettings.Alerts: событие возникает только при удержании условия
    /// не меньше DurationSeconds, повторно — не раньше CooldownSeconds с прошлого события.
    /// Состояние каждого правила хранится между вызовами Evaluate; Reset() сбрасывает его.
    /// </summary>
    public sealed class AlertEngine
    {
        private sealed class RuleState
        {
            public DateTime? ConditionSince;
            public DateTime? LastFired;
        }

        private readonly object _gate = new object();
        private readonly List<AlertRule> _rules;
        private readonly bool _enabled;
        private readonly Dictionary<AlertRule, RuleState> _states;

        public AlertEngine(AppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }
            _enabled = settings.AlertsEnabled;
            _rules = settings.Alerts ?? new List<AlertRule>();
            _states = new Dictionary<AlertRule, RuleState>();
        }

        public void Reset()
        {
            lock (_gate)
            {
                _states.Clear();
            }
        }

        public IList<AlertEvent> Evaluate(SystemSnapshot snapshot)
        {
            List<AlertEvent> events = new List<AlertEvent>();
            if (snapshot == null || !_enabled)
            {
                return events;
            }

            lock (_gate)
            {
                for (int i = 0; i < _rules.Count; i++)
                {
                    AlertRule rule = _rules[i];
                    if (rule == null || !rule.Enabled)
                    {
                        continue;
                    }

                    bool hasValue;
                    double value = GetValue(snapshot, rule.Metric, out hasValue);
                    bool condition = false;
                    if (hasValue)
                    {
                        condition = rule.TriggerAbove ? value >= rule.Threshold : value <= rule.Threshold;
                    }

                    RuleState state;
                    if (!_states.TryGetValue(rule, out state))
                    {
                        state = new RuleState();
                        _states[rule] = state;
                    }

                    if (!condition)
                    {
                        state.ConditionSince = null;
                        continue;
                    }
                    if (state.ConditionSince == null)
                    {
                        state.ConditionSince = snapshot.Timestamp;
                    }

                    double held = (snapshot.Timestamp - state.ConditionSince.Value).TotalSeconds;
                    int duration = rule.DurationSeconds > 0 ? rule.DurationSeconds : 0;
                    if (held + 0.001 < duration)
                    {
                        continue;
                    }

                    int cooldown = rule.CooldownSeconds > 0 ? rule.CooldownSeconds : 0;
                    if (state.LastFired != null)
                    {
                        double sinceFired = (snapshot.Timestamp - state.LastFired.Value).TotalSeconds;
                        if (sinceFired + 0.001 < cooldown)
                        {
                            continue;
                        }
                    }

                    AlertEvent alert = new AlertEvent();
                    alert.Metric = rule.Metric;
                    alert.Title = "Превышение: " + MetricName(rule.Metric);
                    alert.Timestamp = snapshot.Timestamp;
                    alert.Value = value;
                    string direction = rule.TriggerAbove ? "выше" : "ниже";
                    alert.Message = string.Format(
                        RussianFormat.Culture,
                        "{0}: {1} {2} порога {3} и удерживается {4} с.",
                        MetricName(rule.Metric),
                        FormatValue(rule.Metric, value),
                        direction,
                        FormatValue(rule.Metric, rule.Threshold),
                        duration);
                    events.Add(alert);
                    state.LastFired = snapshot.Timestamp;
                }
            }
            return events;
        }

        private static double GetValue(SystemSnapshot snapshot, AlertMetric metric, out bool hasValue)
        {
            switch (metric)
            {
                case AlertMetric.Cpu:
                    hasValue = true;
                    return snapshot.CpuUsage;
                case AlertMetric.Memory:
                    hasValue = true;
                    return snapshot.MemoryUsage;
                case AlertMetric.Disk:
                    hasValue = true;
                    return snapshot.DiskActive;
                case AlertMetric.Temperature:
                    hasValue = snapshot.CpuTemperature.HasValue;
                    return snapshot.CpuTemperature.HasValue ? snapshot.CpuTemperature.Value : 0.0;
                case AlertMetric.SystemDriveFree:
                    hasValue = true;
                    return snapshot.SystemDriveFreePercent;
                case AlertMetric.PageFile:
                    hasValue = true;
                    return snapshot.PageFileUsage;
                default:
                    hasValue = false;
                    return 0.0;
            }
        }

        private static string MetricName(AlertMetric metric)
        {
            switch (metric)
            {
                case AlertMetric.Cpu:
                    return "Процессор";
                case AlertMetric.Memory:
                    return "Память";
                case AlertMetric.Disk:
                    return "Диск";
                case AlertMetric.Temperature:
                    return "Температура ЦП";
                case AlertMetric.SystemDriveFree:
                    return "Системный диск";
                case AlertMetric.PageFile:
                    return "Файл подкачки";
                default:
                    return metric.ToString();
            }
        }

        private static string FormatValue(AlertMetric metric, double value)
        {
            if (metric == AlertMetric.Temperature)
            {
                return RussianFormat.Number(value) + " °C";
            }
            return RussianFormat.Percent(value);
        }
    }
}
