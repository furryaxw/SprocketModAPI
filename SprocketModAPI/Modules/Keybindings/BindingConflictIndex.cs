using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SprocketModAPI
{
    internal enum BindingConflictSourceKind
    {
        Mod,
        Native
    }

    internal sealed class BindingConflictInfo
    {
        internal BindingConflictInfo(string stableActionId, int slot, KeyChord binding,
            BindingConflictSourceKind sourceKind, string sourceId, string sourceName)
        {
            StableActionId = stableActionId;
            Slot = slot;
            Binding = binding;
            SourceKind = sourceKind;
            SourceId = sourceId;
            SourceName = sourceName;
        }

        internal string StableActionId { get; }
        internal int Slot { get; }
        internal KeyChord Binding { get; }
        internal BindingConflictSourceKind SourceKind { get; }
        internal string SourceId { get; }
        internal string SourceName { get; }
    }

    internal sealed class ModBindingSnapshot
    {
        internal ModBindingSnapshot(string stableActionId, string displayName, KeyChord primary, KeyChord secondary)
        {
            StableActionId = stableActionId;
            DisplayName = displayName;
            Primary = primary;
            Secondary = secondary;
        }

        internal string StableActionId { get; }
        internal string DisplayName { get; }
        internal KeyChord Primary { get; }
        internal KeyChord Secondary { get; }
    }

    internal sealed class NativeBindingSnapshot
    {
        internal NativeBindingSnapshot(string sourceId, string sourceName, KeyChord binding)
        {
            SourceId = sourceId;
            SourceName = sourceName;
            Binding = binding;
        }

        internal string SourceId { get; }
        internal string SourceName { get; }
        internal KeyChord Binding { get; }
    }

    internal sealed class BindingConflictIndex
    {
        private readonly Dictionary<string, IReadOnlyList<BindingConflictInfo>> byAction = new(StringComparer.Ordinal);

        internal void Rebuild(IEnumerable<ModBindingSnapshot> modBindings, IEnumerable<NativeBindingSnapshot> nativeBindings)
        {
            var modEntries = new List<ModEntry>();
            foreach (ModBindingSnapshot action in modBindings)
            {
                if (!action.Primary.IsEmpty)
                    modEntries.Add(new ModEntry(action, 0, action.Primary));
                if (!action.Secondary.IsEmpty)
                    modEntries.Add(new ModEntry(action, 1, action.Secondary));
            }

            var conflicts = modEntries.ToDictionary(
                entry => (entry.Action.StableActionId, entry.Slot),
                _ => new List<BindingConflictInfo>());

            foreach (IGrouping<KeyChord, ModEntry> group in modEntries.GroupBy(entry => entry.Binding))
            {
                ModEntry[] entries = group.ToArray();
                if (entries.Length < 2)
                    continue;
                foreach (ModEntry target in entries)
                    foreach (ModEntry source in entries)
                        if (!ReferenceEquals(target, source))
                            conflicts[(target.Action.StableActionId, target.Slot)].Add(new BindingConflictInfo(
                                target.Action.StableActionId, target.Slot, target.Binding,
                                BindingConflictSourceKind.Mod, source.Action.StableActionId, source.Action.DisplayName));
            }

            var nativeByBinding = nativeBindings
                .GroupBy(binding => binding.Binding)
                .ToDictionary(group => group.Key, group => group
                    .GroupBy(binding => binding.SourceId, StringComparer.Ordinal)
                    .Select(source => source.First())
                    .ToArray());

            foreach (ModEntry target in modEntries)
            {
                if (!nativeByBinding.TryGetValue(target.Binding, out NativeBindingSnapshot[]? nativeMatches))
                    continue;
                foreach (NativeBindingSnapshot source in nativeMatches)
                    conflicts[(target.Action.StableActionId, target.Slot)].Add(new BindingConflictInfo(
                        target.Action.StableActionId, target.Slot, target.Binding,
                        BindingConflictSourceKind.Native, source.SourceId, source.SourceName));
            }

            byAction.Clear();
            foreach (IGrouping<string, KeyValuePair<(string ActionId, int Slot), List<BindingConflictInfo>>> actionGroup
                in conflicts.Where(entry => entry.Value.Count != 0).GroupBy(entry => entry.Key.StableActionId, StringComparer.Ordinal))
            {
                byAction[actionGroup.Key] = actionGroup
                    .SelectMany(entry => entry.Value)
                    .OrderBy(conflict => conflict.Slot)
                    .ThenBy(conflict => conflict.SourceKind)
                    .ThenBy(conflict => conflict.SourceName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        internal IReadOnlyList<BindingConflictInfo> GetForAction(string stableActionId)
            => byAction.TryGetValue(stableActionId, out IReadOnlyList<BindingConflictInfo>? conflicts)
                ? conflicts
                : Array.Empty<BindingConflictInfo>();

        private sealed class ModEntry
        {
            internal ModEntry(ModBindingSnapshot action, int slot, KeyChord binding)
            {
                Action = action;
                Slot = slot;
                Binding = binding;
            }

            internal ModBindingSnapshot Action { get; }
            internal int Slot { get; }
            internal KeyChord Binding { get; }
        }
    }

    internal sealed class NativeInputBindingReader
    {
        private readonly Action<string> warn;
        private bool warnedNoAssets;

        internal NativeInputBindingReader(Action<string> warn)
        {
            this.warn = warn;
        }

        internal IReadOnlyList<NativeBindingSnapshot> ReadLoadedAssets()
        {
            var result = new List<NativeBindingSnapshot>();
            InputActionAsset[] assets;
            try
            {
                assets = Resources.FindObjectsOfTypeAll<InputActionAsset>();
            }
            catch (Exception exception)
            {
                warn($"[SMA-KEY] failed to enumerate native input assets: {exception.Message}");
                return result;
            }

            if (assets.Length == 0)
            {
                if (!warnedNoAssets)
                {
                    warnedNoAssets = true;
                    warn("[SMA-KEY] no loaded InputActionAsset was found; native keybinding conflicts are unavailable.");
                }
                return result;
            }
            warnedNoAssets = false;

            foreach (InputActionAsset asset in assets)
            {
                if (asset == null)
                    continue;
                try
                {
                    foreach (InputActionMap map in asset.actionMaps)
                        foreach (InputAction action in map.actions)
                            ReadAction(asset, map, action, result);
                }
                catch (Exception exception)
                {
                    warn($"[SMA-KEY] failed to read native input asset {asset.name}: {exception.Message}");
                }
            }
            return result;
        }

        private static void ReadAction(InputActionAsset asset, InputActionMap map, InputAction action,
            List<NativeBindingSnapshot> result)
        {
            string sourceId = $"{asset.name}:{map.name}:{action.name}";
            string sourceName = $"GAME {map.name}/{action.name}";
            var bindings = action.bindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                InputBinding binding = bindings[index];
                if (binding.isComposite)
                {
                    ModifierKeys modifiers = ModifierKeys.None;
                    string? primaryPath = null;
                    int partIndex = index + 1;
                    while (partIndex < bindings.Count && bindings[partIndex].isPartOfComposite)
                    {
                        InputBinding part = bindings[partIndex];
                        string partName = part.name ?? "";
                        string? path = part.effectivePath;
                        if (partName.StartsWith("modifier", StringComparison.OrdinalIgnoreCase))
                            modifiers |= ModifierForPath(path);
                        else if (partName.Equals("binding", StringComparison.OrdinalIgnoreCase))
                            primaryPath = path;
                        partIndex++;
                    }

                    if (modifiers != ModifierKeys.None && TryCreateSupportedChord(primaryPath, modifiers, out KeyChord compositeChord))
                        result.Add(new NativeBindingSnapshot(sourceId, sourceName, compositeChord));
                    index = partIndex - 1;
                    continue;
                }

                if (!binding.isPartOfComposite && TryCreateSupportedChord(binding.effectivePath, ModifierKeys.None, out KeyChord chord))
                    result.Add(new NativeBindingSnapshot(sourceId, sourceName, chord));
            }
        }

        private static bool TryCreateSupportedChord(string? path, ModifierKeys modifiers, out KeyChord chord)
        {
            chord = default;
            if (string.IsNullOrWhiteSpace(path)
                || (!path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase)
                    && !path.StartsWith("<Mouse>/", StringComparison.OrdinalIgnoreCase)))
                return false;
            try
            {
                chord = new KeyChord(path, modifiers);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static ModifierKeys ModifierForPath(string? path)
            => path?.ToLowerInvariant() switch
            {
                "<keyboard>/leftshift" => ModifierKeys.LeftShift,
                "<keyboard>/rightshift" => ModifierKeys.RightShift,
                "<keyboard>/leftctrl" => ModifierKeys.LeftCtrl,
                "<keyboard>/rightctrl" => ModifierKeys.RightCtrl,
                "<keyboard>/leftalt" => ModifierKeys.LeftAlt,
                "<keyboard>/rightalt" => ModifierKeys.RightAlt,
                _ => ModifierKeys.None
            };
    }
}
