using System.Runtime.InteropServices;

namespace HaelpMi.UI.Interop;

/// <summary>
/// Minimaler Vista-Style-Datei-Dialog über COM-Interop, einziger Zweck: FOS_DONTADDTORECENT
/// setzen. Microsoft.Win32.OpenFileDialog/SaveFileDialog unterstützen dieses Flag nicht
/// (bestätigt: dotnet/winforms#5405 - dieselbe Einschränkung gilt für die WPF-Pendants, auch
/// über Reflection nicht erreichbar). Ohne das Flag landet jede über den Standard-Dialog
/// geöffnete/gespeicherte Datei automatisch als Verknüpfung in "Zuletzt verwendet"
/// (SHAddToRecentDocs) - das hatte beim Lizenz-Workflow zu einem Windows-Defender-Fund auf
/// genau so einer Verknüpfung geführt (Issue #18-Diskussion).
///
/// Bewusst nur die Basisschnittstelle IFileDialog deklariert, nicht die abgeleiteten
/// IFileOpenDialog/IFileSaveDialog - deren eigene AddPlace-Überladung ersetzt einen
/// Basis-vtable-Slot mit einem anderen Parametertyp, was ohne Fehlerrisiko in C# nicht
/// einfach nachzubilden ist. IFileDialog allein reicht für Show/SetFileTypes/SetOptions/
/// SetFileName/SetTitle/SetDefaultExtension/GetResult - mehr wird hier nicht gebraucht.
/// Interface-Layout und GUIDs stammen aus Microsofts eigenem WinForms-Referenzquellcode
/// (FileDialog_Vista_Interop.cs), nicht selbst geraten.
/// </summary>
internal static class NoRecentFileDialog
{
    private const uint FosForceFileSystem = 0x00000040;
    private const uint FosFileMustExist = 0x00001000;
    private const uint FosOverwritePrompt = 0x00000002;
    private const uint FosDontAddToRecent = 0x02000000;

    public static string? ShowOpen(IntPtr ownerHwnd, string title, params (string Name, string Spec)[] filters)
    {
        var dialog = (IFileDialog)new FileOpenDialogRcw();
        try
        {
            dialog.SetTitle(title);
            dialog.SetFileTypes((uint)filters.Length, ToFilterSpecs(filters));
            dialog.SetOptions(FosForceFileSystem | FosFileMustExist | FosDontAddToRecent);
            return dialog.Show(ownerHwnd) == 0 ? GetResultPath(dialog) : null;
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    public static string? ShowSave(IntPtr ownerHwnd, string title, string suggestedFileName, string defaultExtension, params (string Name, string Spec)[] filters)
    {
        var dialog = (IFileDialog)new FileSaveDialogRcw();
        try
        {
            dialog.SetTitle(title);
            dialog.SetFileName(suggestedFileName);
            dialog.SetDefaultExtension(defaultExtension);
            dialog.SetFileTypes((uint)filters.Length, ToFilterSpecs(filters));
            dialog.SetOptions(FosForceFileSystem | FosOverwritePrompt | FosDontAddToRecent);
            return dialog.Show(ownerHwnd) == 0 ? GetResultPath(dialog) : null;
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    private static string GetResultPath(IFileDialog dialog)
    {
        dialog.GetResult(out var item);
        item.GetDisplayName(Sigdn.FileSysPath, out var path);
        return path;
    }

    private static COMDLG_FILTERSPEC[] ToFilterSpecs((string Name, string Spec)[] filters) =>
        Array.ConvertAll(filters, f => new COMDLG_FILTERSPEC { pszName = f.Name, pszSpec = f.Spec });

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRcw;

    [ComImport, Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]
    private class FileSaveDialogRcw;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto, Pack = 4)]
    private struct COMDLG_FILTERSPEC
    {
        [MarshalAs(UnmanagedType.LPWStr)] internal string pszName;
        [MarshalAs(UnmanagedType.LPWStr)] internal string pszSpec;
    }

    private enum Sigdn : uint
    {
        FileSysPath = 0x80058000,
    }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show([In] IntPtr parent);
        void SetFileTypes([In] uint cFileTypes, [In, MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        void SetFileTypeIndex([In] uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise([In, MarshalAs(UnmanagedType.Interface)] object pfde, out uint pdwCookie);
        void Unadvise([In] uint dwCookie);
        void SetOptions([In] uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi);
        void SetFolder([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi);
        void GetFolder([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        void GetCurrentSelection([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        void SetFileName([In, MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([In, MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        void AddPlace([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi, int alignment);
        void SetDefaultExtension([In, MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close([MarshalAs(UnmanagedType.Error)] int hr);
        void SetClientGuid([In] ref Guid guid);
        void ClearClientData();
        void SetFilter([MarshalAs(UnmanagedType.Interface)] IntPtr pFilter);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler([In, MarshalAs(UnmanagedType.Interface)] IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
        void GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        void GetDisplayName([In] Sigdn sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes([In] uint sfgaoMask, out uint psfgaoAttribs);
        void Compare([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi, [In] uint hint, out int piOrder);
    }
}
