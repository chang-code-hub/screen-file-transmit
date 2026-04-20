using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace screen_file_transmit
{
    public class FolderPicker
    {
        public string Title { get; set; }
        public string SelectedPath { get; set; }

        public bool? ShowDialog(Window owner = null)
        {
            var dialog = (IFileDialog)new FileOpenDialog();
            dialog.SetOptions(FOS.PICKFOLDERS | FOS.FORCEFILESYSTEM);

            if (!string.IsNullOrEmpty(Title))
                dialog.SetTitle(Title);

            if (!string.IsNullOrEmpty(SelectedPath))
            {
                if (SHCreateItemFromParsingName(SelectedPath, IntPtr.Zero, typeof(IShellItem).GUID, out var item) == 0)
                {
                    dialog.SetFolder(item);
                }
            }

            var hwnd = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
            var result = dialog.Show(hwnd);

            if (result == 0)
            {
                dialog.GetResult(out var item);
                item.GetDisplayName(SIGDN.FILESYSPATH, out var path);
                SelectedPath = path;
                return true;
            }

            return false;
        }

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialog { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show([In] IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(int iFileType);
            void GetFileTypeIndex(out int piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(FOS fos);
            void GetOptions(out FOS pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([In, MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([In, MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int alignment);
            void SetDefaultExtension([In, MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [Flags]
        private enum FOS : uint
        {
            PICKFOLDERS = 0x00000020,
            FORCEFILESYSTEM = 0x00000040,
        }

        private enum SIGDN : uint
        {
            FILESYSPATH = 0x80058000,
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem ppv);
    }
}
