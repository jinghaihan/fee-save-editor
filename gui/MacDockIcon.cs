using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace FeeEditor.Gui;

internal static class MacDockIcon
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    internal static void Apply()
    {
        if (!OperatingSystem.IsMacOS())
            return;
        using var stream = AssetLoader.Open(new Uri("avares://FeeEditor.Gui/Assets/app-icon.png"));
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        byte[] pixels = buffer.ToArray();
        IntPtr bytes = Marshal.AllocHGlobal(pixels.Length);
        try
        {
            Marshal.Copy(pixels, 0, bytes, pixels.Length);
            IntPtr data = Send(objc_getClass("NSData"), sel_registerName("dataWithBytes:length:"), bytes, (nuint)pixels.Length);
            IntPtr image = Send(Send(objc_getClass("NSImage"), sel_registerName("alloc")), sel_registerName("initWithData:"), data);
            if (image == IntPtr.Zero)
                throw new InvalidOperationException("Could not load the macOS Dock icon.");
            try
            {
                IntPtr application = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                Send(application, sel_registerName("setApplicationIconImage:"), image);
            }
            finally
            {
                Send(image, sel_registerName("release"));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(bytes);
        }
    }

    [DllImport(ObjectiveCLibrary)]
    private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjectiveCLibrary)]
    private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);
    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);
}
