using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SprocketModAPI
{
    // 声明式配置服务：校验注册、加载/持久化值、发布变更。
    // 单个模组的畸形定义或损坏文件不得影响其他模组。
    internal sealed class ModConfigService : IModConfigService, IDisposable
    {
        private readonly Dictionary<string, Registration> registrations = new(StringComparer.Ordinal);
        private readonly ModConfigStore store;
        private readonly Action<string> warn;
        private bool disposed;

        internal ModConfigService(string rootDirectory, Action<string> warn, Func<DateTime>? utcNow = null)
        {
            this.warn = warn ?? throw new ArgumentNullException(nameof(warn));
            store = new ModConfigStore(rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory)), warn, utcNow);
        }

        public event Action<ModConfigChangedEventArgs>? Changed;
        public event Action? RegistrationsChanged;

        public IReadOnlyList<ModConfigSnapshot> Snapshots
        {
            get
            {
                if (disposed)
                    return Array.Empty<ModConfigSnapshot>();

                var snapshots = new List<ModConfigSnapshot>(registrations.Count);
                foreach (Registration registration in registrations.Values)
                    snapshots.Add(registration.BuildSnapshot());

                snapshots.Sort(static (left, right) =>
                {
                    int byName = string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
                    return byName != 0 ? byName : string.Compare(left.ModId, right.ModId, StringComparison.Ordinal);
                });

                return snapshots;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)] // GetCallingAssembly 需要真实栈帧：不能被内联进模组方法
        public IModConfigRegistration Register(ModConfigDefinition definition)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ModConfigService));
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            if (string.IsNullOrWhiteSpace(definition.ModId))
                definition.ModId = ModIdentity.ResolveModId(Assembly.GetCallingAssembly());

            Validate(definition);

            if (registrations.ContainsKey(definition.ModId))
            {
                string message = $"[SMA-CONFIG] duplicate mod config registration rejected: {definition.ModId}.";
                warn(message);
                throw new InvalidOperationException(message);
            }

            var registration = new Registration(this, store, definition, warn);
            registrations.Add(definition.ModId, registration);
            RegistrationsChanged?.Invoke();
            return registration;
        }

        public IModConfigRegistration? Find(string modId)
        {
            if (disposed || string.IsNullOrEmpty(modId))
                return null;
            return registrations.TryGetValue(modId, out Registration? registration) ? registration : null;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            Registration[] snapshot = registrations.Values.ToArray();
            registrations.Clear();
            foreach (Registration registration in snapshot)
                registration.Detach();

            Changed = null;
            RegistrationsChanged = null;
        }

        internal void NotifyChanged(string modId, string key)
        {
            if (!disposed)
                Changed?.Invoke(new ModConfigChangedEventArgs(modId, key));
        }

        private void RemoveRegistration(Registration registration)
        {
            if (registrations.TryGetValue(registration.ModId, out Registration? current)
                && ReferenceEquals(current, registration))
            {
                registrations.Remove(registration.ModId);
                RegistrationsChanged?.Invoke();
            }
        }

        // 校验声明式定义；任何接入错误都立刻抛出，而不是留到菜单渲染时才发现。
        private static void Validate(ModConfigDefinition definition)
        {
            if (!IsStableId(definition.ModId))
                throw new ArgumentException($"Invalid mod config ModId: '{definition.ModId}'.", nameof(definition));

            var sectionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModConfigSectionDefinition section in definition.Sections ?? Array.Empty<ModConfigSectionDefinition>())
            {
                if (!IsStableId(section.Id))
                    throw new ArgumentException($"Invalid mod config section id: '{section.Id}'.", nameof(definition));
                if (!sectionIds.Add(section.Id))
                    throw new ArgumentException($"Duplicate mod config section id: '{section.Id}'.", nameof(definition));
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModConfigEntryDefinition entry in definition.Entries ?? Array.Empty<ModConfigEntryDefinition>())
            {
                if (!IsStableId(entry.Key))
                    throw new ArgumentException($"Invalid mod config entry key: '{entry.Key}'.", nameof(definition));
                if (!keys.Add(entry.Key))
                    throw new ArgumentException($"Duplicate mod config entry key: '{entry.Key}'.", nameof(definition));
                if (entry.SectionId.Length != 0 && !sectionIds.Contains(entry.SectionId))
                    throw new ArgumentException($"Entry '{entry.Key}' references unknown section '{entry.SectionId}'.", nameof(definition));

                switch (entry.Kind)
                {
                    case ModConfigEntryKind.Slider:
                        if (!(entry.Minimum < entry.Maximum))
                            throw new ArgumentException($"Slider '{entry.Key}' requires Minimum < Maximum.", nameof(definition));
                        if (!(entry.Step > 0))
                            throw new ArgumentException($"Slider '{entry.Key}' requires Step > 0.", nameof(definition));
                        if (entry.DefaultNumber < entry.Minimum || entry.DefaultNumber > entry.Maximum)
                            throw new ArgumentException($"Slider '{entry.Key}' default is outside its range.", nameof(definition));
                        break;
                    case ModConfigEntryKind.Choice:
                        if (entry.Options.Count == 0)
                            throw new ArgumentException($"Choice '{entry.Key}' requires at least one option.", nameof(definition));
                        if (entry.Options.Distinct(StringComparer.Ordinal).Count() != entry.Options.Count)
                            throw new ArgumentException($"Choice '{entry.Key}' contains duplicate options.", nameof(definition));
                        if (!entry.Options.Contains(entry.DefaultText, StringComparer.Ordinal))
                            throw new ArgumentException($"Choice '{entry.Key}' default is not one of its options.", nameof(definition));
                        break;
                    case ModConfigEntryKind.Text:
                        if (entry.MaxLength <= 0)
                            throw new ArgumentException($"Text '{entry.Key}' requires MaxLength > 0.", nameof(definition));
                        if (entry.DefaultText.Length > entry.MaxLength)
                            throw new ArgumentException($"Text '{entry.Key}' default exceeds MaxLength.", nameof(definition));
                        break;
                    case ModConfigEntryKind.Toggle:
                        break;
                    default:
                        throw new ArgumentException($"Unsupported mod config entry kind for '{entry.Key}'.", nameof(definition));
                }
            }
        }

        // 稳定 ID/键：字母、数字、`-`、`_`、`.`，长度 1..64。
        private static bool IsStableId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
                return false;

            foreach (char character in value)
            {
                bool allowed = char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.';
                if (!allowed)
                    return false;
            }

            return true;
        }

        private sealed class Registration : IModConfigRegistration
        {
            private readonly ModConfigService service;
            private readonly ModConfigStore store;
            private readonly Action<string> warn;
            private readonly Dictionary<string, ModConfigEntryDefinition> definitions = new(StringComparer.Ordinal);
            private readonly List<ModConfigEntryDefinition> orderedEntries;
            private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);
            private readonly Dictionary<string, object> retained = new(StringComparer.Ordinal);
            private bool disposed;

            internal Registration(ModConfigService service, ModConfigStore store, ModConfigDefinition definition, Action<string> warn)
            {
                this.service = service;
                this.store = store;
                this.warn = warn;
                ModId = definition.ModId;
                DisplayName = string.IsNullOrEmpty(definition.DisplayName) ? definition.ModId : definition.DisplayName;
                Sections = definition.Sections ?? Array.Empty<ModConfigSectionDefinition>();
                orderedEntries = (definition.Entries ?? Array.Empty<ModConfigEntryDefinition>()).ToList();
                foreach (ModConfigEntryDefinition entry in orderedEntries)
                    definitions[entry.Key] = entry;

                Load();
            }

            internal string ModId { get; }
            private string DisplayName { get; }
            private IReadOnlyList<ModConfigSectionDefinition> Sections { get; }

            public bool IsDisposed => disposed;

            public ModConfigSnapshot Snapshot
            {
                get
                {
                    var entries = new List<ModConfigEntrySnapshot>(orderedEntries.Count);
                    foreach (ModConfigEntryDefinition definition in orderedEntries)
                        entries.Add(BuildEntrySnapshot(definition));

                    return new ModConfigSnapshot
                    {
                        ModId = ModId,
                        DisplayName = DisplayName,
                        Sections = Sections,
                        Entries = entries
                    };
                }
            }

            internal ModConfigSnapshot BuildSnapshot() => Snapshot;

            public bool GetBool(string key)
            {
                object value = Require(key, ModConfigEntryKind.Toggle);
                return (bool)value;
            }

            public double GetNumber(string key)
            {
                object value = Require(key, ModConfigEntryKind.Slider);
                return (double)value;
            }

            public string GetText(string key)
            {
                object value = Require(key, ModConfigEntryKind.Text, ModConfigEntryKind.Choice);
                return (string)value;
            }

            public void SetBool(string key, bool value)
            {
                ModConfigEntryDefinition definition = RequireDefinition(key, ModConfigEntryKind.Toggle);
                Apply(definition, value);
            }

            public void SetNumber(string key, double value)
            {
                ModConfigEntryDefinition definition = RequireDefinition(key, ModConfigEntryKind.Slider);
                if (double.IsNaN(value) || value < definition.Minimum || value > definition.Maximum)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        $"'{key}' must be within [{definition.Minimum}, {definition.Maximum}].");

                Apply(definition, value);
            }

            public void SetText(string key, string value)
            {
                ModConfigEntryDefinition definition = RequireDefinition(key, ModConfigEntryKind.Text, ModConfigEntryKind.Choice);
                string candidate = value ?? "";
                if (definition.Kind == ModConfigEntryKind.Choice && !definition.Options.Contains(candidate, StringComparer.Ordinal))
                    throw new ArgumentException($"'{key}' must be one of: {string.Join(", ", definition.Options)}.", nameof(value));
                if (definition.Kind == ModConfigEntryKind.Text && candidate.Length > definition.MaxLength)
                    throw new ArgumentOutOfRangeException(nameof(value), $"'{key}' exceeds MaxLength {definition.MaxLength}.");

                Apply(definition, candidate);
            }

            public void ResetToDefault(string key)
            {
                ModConfigEntryDefinition definition = RequireDefinition(key);
                Apply(definition, definition.DefaultValue);
            }

            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                service.RemoveRegistration(this);
            }

            internal void Detach() => disposed = true;

            private void Load()
            {
                Dictionary<string, object> stored = store.Load(ModId);
                foreach (ModConfigEntryDefinition definition in orderedEntries)
                {
                    if (stored.TryGetValue(definition.Key, out object? raw) && TryCoerce(definition, raw, out object? coerced))
                    {
                        values[definition.Key] = coerced!;
                        continue;
                    }

                    values[definition.Key] = definition.DefaultValue;
                }

                foreach (KeyValuePair<string, object> pair in stored)
                {
                    if (!definitions.ContainsKey(pair.Key))
                        retained[pair.Key] = pair.Value;
                }
            }

            // 把文件中读到的值适配到当前定义；类型不符或超出范围时降级为默认值并告警。
            private bool TryCoerce(ModConfigEntryDefinition definition, object? raw, out object? value)
            {
                value = null;
                switch (definition.Kind)
                {
                    case ModConfigEntryKind.Toggle:
                        if (raw is bool flag)
                        {
                            value = flag;
                            return true;
                        }
                        break;
                    case ModConfigEntryKind.Slider:
                        if (raw is double number)
                        {
                            double clamped = Math.Clamp(number, definition.Minimum, definition.Maximum);
                            if (clamped != number)
                                warn($"[SMA-CONFIG] {ModId}:{definition.Key} was clamped from {number} to {clamped}.");
                            value = clamped;
                            return true;
                        }
                        break;
                    case ModConfigEntryKind.Choice:
                        if (raw is string choice && definition.Options.Contains(choice, StringComparer.Ordinal))
                        {
                            value = choice;
                            return true;
                        }
                        break;
                    case ModConfigEntryKind.Text:
                        if (raw is string text && text.Length <= definition.MaxLength)
                        {
                            value = text;
                            return true;
                        }
                        break;
                }

                warn($"[SMA-CONFIG] {ModId}:{definition.Key} stored value does not match its definition; using the default.");
                return false;
            }

            private void Apply(ModConfigEntryDefinition definition, object candidate)
            {
                if (disposed)
                    throw new ObjectDisposedException(nameof(IModConfigRegistration));

                object current = values[definition.Key];
                if (current is bool currentFlag && candidate is bool candidateFlag && currentFlag == candidateFlag)
                    return;
                if (current is double currentNumber && candidate is double candidateNumber && currentNumber.Equals(candidateNumber))
                    return;
                if (current is string currentText && candidate is string candidateText && string.Equals(currentText, candidateText, StringComparison.Ordinal))
                    return;

                values[definition.Key] = candidate;
                Persist();
                service.NotifyChanged(ModId, definition.Key);
            }

            private void Persist()
            {
                var merged = new Dictionary<string, object>(retained, StringComparer.Ordinal);
                foreach (KeyValuePair<string, object> pair in values)
                    merged[pair.Key] = pair.Value;
                store.Save(ModId, merged);
            }

            private object Require(string key, params ModConfigEntryKind[] kinds)
            {
                ModConfigEntryDefinition definition = RequireDefinition(key, kinds);
                return values[definition.Key];
            }

            private ModConfigEntryDefinition RequireDefinition(string key, params ModConfigEntryKind[] kinds)
            {
                if (disposed)
                    throw new ObjectDisposedException(nameof(IModConfigRegistration));
                if (string.IsNullOrEmpty(key) || !definitions.TryGetValue(key, out ModConfigEntryDefinition? definition))
                    throw new ArgumentException($"Unknown mod config key '{key}' for {ModId}.", nameof(key));

                if (kinds.Length != 0 && !kinds.Contains(definition.Kind))
                    throw new InvalidOperationException(
                        $"Mod config key '{key}' is {definition.Kind}, not {string.Join("/", kinds)}.");

                return definition;
            }

            private ModConfigEntrySnapshot BuildEntrySnapshot(ModConfigEntryDefinition definition)
            {
                object value = values[definition.Key];
                return new ModConfigEntrySnapshot
                {
                    Definition = definition,
                    BoolValue = value as bool? ?? false,
                    NumberValue = value as double? ?? 0d,
                    TextValue = value as string ?? ""
                };
            }
        }
    }
}
