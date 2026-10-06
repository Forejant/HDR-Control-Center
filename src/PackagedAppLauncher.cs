using System;
using System.Runtime.InteropServices;

namespace HdrCenter {
[ComImport,Guid("2e941141-7f97-4756-ba1d-9decde894a3d"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IApplicationActivationManager {
    [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appId,[MarshalAs(UnmanagedType.LPWStr)] string arguments,uint options,out uint processId);
    [PreserveSig] int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appId,IntPtr items,[MarshalAs(UnmanagedType.LPWStr)] string verb,out uint processId);
    [PreserveSig] int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appId,IntPtr items,out uint processId);
}
public static class PackagedAppLauncher {
    public const string NvidiaId="NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIACorp.NVIDIAControlPanel";
    public static bool TryOpenNvidia(out string error) {
        object manager=null;
        try {
            manager=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")));
            uint pid; int hr=((IApplicationActivationManager)manager).ActivateApplication(NvidiaId,null,2,out pid);
            if(hr<0) { error=Marshal.GetExceptionForHR(hr).Message; return false; }
            error=null; return true;
        } catch(Exception ex) { error=ex.Message; return false; }
        finally { if(manager!=null && Marshal.IsComObject(manager))Marshal.ReleaseComObject(manager); }
    }
}
}
