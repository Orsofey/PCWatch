using System;
using System.Runtime.Serialization;
using Microsoft.Win32;

namespace PCWatch.Core
{
    public enum StartupItemKind
    {
        RegistryRun,
        StartupFolder,
        Service,
        ScheduledTask
    }

    public sealed class StartupRegistryRoot
    {
        public RegistryHive Hive { get; set; }
        public RegistryView View { get; set; }
        public string SubKey { get; set; }
        public bool RequiresAdmin { get; set; }

        public StartupRegistryRoot()
        {
            SubKey = "";
        }
    }

    public sealed class StartupItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public StartupItemKind Kind { get; set; }
        public string Location { get; set; }
        public string Command { get; set; }
        public bool Enabled { get; set; }
        public bool RequiresAdmin { get; set; }
        public bool CanToggle { get; set; }
        public string ProtectionReason { get; set; }
        public string Publisher { get; set; }
        public string SignatureStatus { get; set; }
        public string IconPath { get; set; }
        public string OriginalStartMode { get; set; }
        public string ProcessState { get; set; }
        public StartupBackup Backup { get; set; }

        public StartupItem()
        {
            Id = "";
            Name = "";
            Location = "";
            Command = "";
            ProtectionReason = "";
            Publisher = "";
            SignatureStatus = "";
            IconPath = "";
            OriginalStartMode = "";
            ProcessState = "";
            CanToggle = true;
            Enabled = true;
        }
    }

    [DataContract]
    public sealed class StartupBackup
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public StartupItemKind Kind { get; set; }
        [DataMember] public string Location { get; set; }
        [DataMember] public string Command { get; set; }
        [DataMember] public RegistryHive Hive { get; set; }
        [DataMember] public RegistryView View { get; set; }
        [DataMember] public string SubKey { get; set; }
        [DataMember] public string ValueName { get; set; }
        [DataMember] public RegistryValueKind ValueKind { get; set; }
        [DataMember] public string BackupPath { get; set; }
        [DataMember] public string OriginalStartMode { get; set; }
        [DataMember] public bool RequiresAdmin { get; set; }
        [DataMember] public string IconPath { get; set; }
        [DataMember] public string Publisher { get; set; }
        [DataMember] public string SignatureStatus { get; set; }
        [DataMember] public DateTime CreatedUtc { get; set; }

        public StartupBackup()
        {
            Id = "";
            Name = "";
            Location = "";
            Command = "";
            SubKey = "";
            ValueName = "";
            BackupPath = "";
            OriginalStartMode = "";
            IconPath = "";
            Publisher = "";
            SignatureStatus = "";
        }
    }
}
