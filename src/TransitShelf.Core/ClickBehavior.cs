namespace TransitShelf.Core;

/// <summary>单击条目的整体行为（FR-70）。默认按类型区分，可整体统一。</summary>
public enum ShelfClickBehavior
{
    /// <summary>文件夹打开，文件 / 文字 / 图片复制（默认）。</summary>
    ByKind,

    /// <summary>所有条目单击都打开。</summary>
    AlwaysOpen,

    /// <summary>所有条目单击都复制（文件夹复制路径）。</summary>
    AlwaysCopy,
}

/// <summary>单击条目最终执行的动作。</summary>
public enum ClickAction
{
    Open,
    Copy,
}

/// <summary>
/// 单击语义（FR-70 / 技术框架设计 6.9）。默认按类型：
/// 文件夹没有"复制内容"语义，单击打开最直接；文件、文字、图片单击复制，
/// 误复制无害且可撤销，误打开则会被别的程序抢走焦点。
/// </summary>
public static class ClickActionResolver
{
    public static ClickAction Resolve(ItemKind kind, ShelfClickBehavior behavior) => behavior switch
    {
        ShelfClickBehavior.AlwaysOpen => ClickAction.Open,
        ShelfClickBehavior.AlwaysCopy => ClickAction.Copy,
        _ => kind == ItemKind.Folder ? ClickAction.Open : ClickAction.Copy,
    };
}
