/*
 * AufBauWerk Erweiterungen für Vivendi
 * Copyright (C) 2024  Manuel Meitinger
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace AufBauWerk.Vivendi.RemoteApp;

[Guid("F8383852-FCD3-11d1-A6B9-006097DF5BD4")]
internal partial class Progress : IDisposable
{
    [Flags]
    internal enum PROGDLG
    {
        NOTIME = 0x00000004,
        NOMINIMIZE = 0x00000008,
        MARQUEEPROGRESS = 0x00000020,
        DONTDISPLAYLOCATIONS = 0x00001000,
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("EBBC7C04-315E-11d2-B62F-006097DF5BD4")]
    internal partial interface IProgressDialog
    {
        void StartProgressDialog(nint windowParent, nint unknownEnableModless, PROGDLG flags, nint reserved = 0);
        void StopProgressDialog();
        void SetTitle(string title);
        void SetAnimation(nint instanceAnimation, uint idAnimation);
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.Bool)]
        bool HasUserCancelled();
        void SetProgress(uint completed, uint total);
        void SetProgress64(ulong completed, ulong total);
        void SetLine(uint lineNumber, string str, [MarshalAs(UnmanagedType.Bool)] bool compactPath, nint reserved = 0);
        void SetCancelMsg(string cancelMsg, nint reserved = 0);
        void Timer(uint timerAction, nint reserved = 0);
    }

    private enum CLSCTX { INPROC_SERVER = 0x1 }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid classId, nint unknownOuter, CLSCTX context, in Guid interfaceId, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IProgressDialog>))] out IProgressDialog ptr);

    private IProgressDialog? _interface;

    public Progress() => Marshal.ThrowExceptionForHR(CoCreateInstance(typeof(Progress).GUID, 0, CLSCTX.INPROC_SERVER, typeof(IProgressDialog).GUID, out _interface));

    unsafe void IDisposable.Dispose()
    {
        if (_interface is not null)
        {
            ((ComObject)(object)_interface).FinalRelease();
            _interface = null;
        }
    }

    private IProgressDialog Interface => _interface ?? throw new ObjectDisposedException(nameof(IProgressDialog));

    public bool IsCancelled => Interface.HasUserCancelled();

    public string Line { set => Interface.SetLine(0, value, compactPath: false); }

    public string Title { set => Interface.SetTitle(value); }

    public nint Window => Win32.GetWindowFromIUnknown(Interface);

    public void Hide() => Interface.StopProgressDialog();

    public void Show() => Interface.StartProgressDialog(0, 0, PROGDLG.NOTIME | PROGDLG.NOMINIMIZE | PROGDLG.MARQUEEPROGRESS | PROGDLG.DONTDISPLAYLOCATIONS);
}
