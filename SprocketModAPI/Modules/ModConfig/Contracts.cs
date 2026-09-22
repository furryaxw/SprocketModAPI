using System;
using System.Collections.Generic;

namespace SprocketModAPI
{
    // 声明式配置条目支持的控件类型。v1 不含键位控件，键位请使用 `IInputService`。
    public enum ModConfigEntryKind
    {
        Toggle = 1,
        Slider = 2,
        Choice = 3,
        Text = 4
    }

    // 配置分组。菜单按声明顺序渲染；`Id` 为空的条目进入未分组区域。
    public sealed class ModConfigSectionDefinition
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public string Description { get; init; } = "";
    }

    // 单个配置条目的定义。请使用 `Toggle`、`Slider`、`Choice`、
    // `Text` 工厂方法构造，不要直接赋值互不相干的字段。
    public sealed class ModConfigEntryDefinition
    {
        public string Key { get; init; } = "";
        public string SectionId { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string Description { get; init; } = "";
        public ModConfigEntryKind Kind { get; init; }

        public bool DefaultBool { get; init; }
        public double DefaultNumber { get; init; }
        public string DefaultText { get; init; } = "";

        public double Minimum { get; init; }
        public double Maximum { get; init; }
        public double Step { get; init; }
        public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();
        public int MaxLength { get; init; } = 128;

        public object DefaultValue => Kind switch
        {
            ModConfigEntryKind.Toggle => DefaultBool,
            ModConfigEntryKind.Slider => DefaultNumber,
            _ => DefaultText
        };

        public static ModConfigEntryDefinition Toggle(string key, string displayName, bool defaultValue,
            string description = "", string sectionId = "")
            => new()
            {
                Key = key,
                DisplayName = displayName,
                Description = description,
                SectionId = sectionId,
                Kind = ModConfigEntryKind.Toggle,
                DefaultBool = defaultValue
            };

        public static ModConfigEntryDefinition Slider(string key, string displayName, double defaultValue,
            double minimum, double maximum, double step = 0.1, string description = "", string sectionId = "")
            => new()
            {
                Key = key,
                DisplayName = displayName,
                Description = description,
                SectionId = sectionId,
                Kind = ModConfigEntryKind.Slider,
                DefaultNumber = defaultValue,
                Minimum = minimum,
                Maximum = maximum,
                Step = step
            };

        public static ModConfigEntryDefinition Choice(string key, string displayName, string defaultValue,
            IReadOnlyList<string> options, string description = "", string sectionId = "")
            => new()
            {
                Key = key,
                DisplayName = displayName,
                Description = description,
                SectionId = sectionId,
                Kind = ModConfigEntryKind.Choice,
                DefaultText = defaultValue,
                Options = options ?? Array.Empty<string>()
            };

        public static ModConfigEntryDefinition Text(string key, string displayName, string defaultValue,
            int maxLength = 128, string description = "", string sectionId = "")
            => new()
            {
                Key = key,
                DisplayName = displayName,
                Description = description,
                SectionId = sectionId,
                Kind = ModConfigEntryKind.Text,
                DefaultText = defaultValue,
                MaxLength = maxLength
            };
    }

    public sealed class ModConfigDefinition
    {
        // 配置命名空间。**可以留空**：留空时由 `IModConfigService` 从调用方程序集的 `Sprocket.Mod.Id` 推断。
        public string ModId { get; set; } = "";
        public string DisplayName { get; init; } = "";
        public IReadOnlyList<ModConfigSectionDefinition> Sections { get; init; } = Array.Empty<ModConfigSectionDefinition>();
        public IReadOnlyList<ModConfigEntryDefinition> Entries { get; init; } = Array.Empty<ModConfigEntryDefinition>();
    }

    public sealed class ModConfigEntrySnapshot
    {
        public ModConfigEntryDefinition Definition { get; init; } = new();
        public bool BoolValue { get; init; }
        public double NumberValue { get; init; }
        public string TextValue { get; init; } = "";

        public object Value => Definition.Kind switch
        {
            ModConfigEntryKind.Toggle => BoolValue,
            ModConfigEntryKind.Slider => NumberValue,
            _ => TextValue
        };
    }

    // 一个模组配置页的只读快照，供菜单渲染。不暴露可修改的内部集合。
    public sealed class ModConfigSnapshot
    {
        public string ModId { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public IReadOnlyList<ModConfigSectionDefinition> Sections { get; init; } = Array.Empty<ModConfigSectionDefinition>();
        public IReadOnlyList<ModConfigEntrySnapshot> Entries { get; init; } = Array.Empty<ModConfigEntrySnapshot>();
    }

    // 配置值实际变化后发布。参数不暴露模组内部对象。
    public sealed class ModConfigChangedEventArgs : EventArgs
    {
        public ModConfigChangedEventArgs(string modId, string key)
        {
            ModId = modId;
            Key = key;
        }

        public string ModId { get; }
        public string Key { get; }
    }

    // 一个模组的配置句柄。读取返回当前生效值，写入立即持久化并发布 `IModConfigService.Changed`。
    // 未知键、类型不匹配或越界写入会抛出明确异常（属于接入错误，应尽早暴露）。
    public interface IModConfigRegistration : IDisposable
    {
        ModConfigSnapshot Snapshot { get; }
        bool IsDisposed { get; }

        bool GetBool(string key);
        double GetNumber(string key);
        string GetText(string key);

        void SetBool(string key, bool value);
        void SetNumber(string key, double value);
        void SetText(string key, string value);
        void ResetToDefault(string key);
    }

    // 声明式模组配置服务。模组注册配置定义，菜单读取 `Snapshots` 渲染，
    // 双方都通过同一个注册句柄读写值，持久化由本服务负责。
    public interface IModConfigService
    {
        IReadOnlyList<ModConfigSnapshot> Snapshots { get; }

        // 某个配置值实际变化后触发（`ModConfigChangedEventArgs.ModId` + `Key`）。
        event Action<ModConfigChangedEventArgs>? Changed;

        // 模组注册或注销配置页后触发；菜单据此重建列表。
        event Action? RegistrationsChanged;

        IModConfigRegistration Register(ModConfigDefinition definition);
        IModConfigRegistration? Find(string modId);
    }
}
