namespace TransitShelf.Core;

/// <summary>条目可用状态。</summary>
public enum ItemState
{
    /// <summary>源文件存在或条目不依赖文件。</summary>
    Ok,

    /// <summary>源文件已被移动或删除。</summary>
    Missing,
}
