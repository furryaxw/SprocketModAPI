using System;

namespace SprocketModAPI
{
    // 游戏内 Mod 菜单。菜单窗口由 API 自己持有；这个接口**只在 API 内部**使用
    // （外加验收程序集 `SprocketModAPI.UiAcceptance` 作为 friend），不是对外 API：
    // 玩家从「设置 → General」的按钮打开，其他模组不需要也不能编程开关它。
    internal interface IModMenuService
    {
        bool IsOpen { get; }

        // 打开菜单（若 UI 不可用则安全失败，不抛异常）。
        void Open();

        void Close();
        void Toggle();

        // 菜单显示状态变化后触发，参数为当前是否可见。
        event Action<bool>? VisibilityChanged;
    }
}
