namespace SprocketModAPI
{
    // 「设置 → General」入口的显示门禁：纯逻辑，**刻意放在没有 UnityEngine 字段的类里**。
    //
    // 为什么单独抽出来：入口的这三个条件（设置菜单激活 / 当前在 General 页 / 菜单窗口没打开）
    // 一旦写错，表现是"按钮该出现时不出现"或"盖在菜单窗口上抢点击"，而这两种毛病在离线环境
    // 完全看不出来。放进纯类之后，离线契约套件能直接断言真值表。（放到 `ModMenuSettingsEntry`
    // 里不行：那个类持有 `Image`/`Button`/TMP 等 Unity 字段，契约进程加载它会去要 UnityEngine.UI
    // 与 Unity.TextMeshPro，而离线套件并不引用它们。）
    internal static class ModMenuEntryVisibility
    {
        // 三个条件缺一不可。最后一条尤其重要——窗口和入口同时可见会互相遮住、还会抢点击。
        internal static bool ShouldShow(bool settingsMenuActive, bool generalPageActive, bool windowVisible)
            => settingsMenuActive && generalPageActive && !windowVisible;

        // 真正 SetActive 前的最后一道闸：没对齐到原生按钮左边界之前不许显示，
        // 否则按钮会停在画布原点（画面左下角外）或闪一下。
        internal static bool ShouldApply(bool requested, bool alignmentReady)
            => requested && alignmentReady;
    }
}
