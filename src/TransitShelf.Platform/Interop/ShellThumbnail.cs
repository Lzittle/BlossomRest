using System.Runtime.InteropServices;

namespace TransitShelf.Platform.Interop;

/// <summary>
/// IShellItemImageFactory（shell32.dll）：取系统缩略图，视频首帧就由它提供。
/// 来源：https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellitemimagefactory
/// </summary>
public static class ShellThumbnail
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Size
    {
        public int Width;
        public int Height;
    }

    /// <summary>SIIGBF：GetImage 的行为标志，只列用到的两项。</summary>
    [Flags]
    public enum GetImageFlags : uint
    {
        /// <summary>SIIGBF_BIGGERSIZEOK：允许返回比请求更大的图。</summary>
        BiggerSizeOk = 0x01,

        /// <summary>SIIGBF_THUMBNAILONLY：只要缩略图，不要图标（拿不到就返回失败）。</summary>
        ThumbnailOnly = 0x08,
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(Size size, GetImageFlags flags, out IntPtr bitmap);
    }

    /// <summary>SHCreateItemFromParsingName：按路径创建 shell 项；失败时抛 <see cref="COMException"/>。</summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory shellItem);

    /// <summary>gdi32.dll 的 DeleteObject：GetImage 返回的 HBITMAP 由调用方释放。</summary>
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool DeleteObject(IntPtr handle);
}
