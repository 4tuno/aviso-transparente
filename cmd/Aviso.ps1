# One-shot native window visibility change. No timer, service or startup entry.
[CmdletBinding()]
param(
    [ValidateSet('ocultar', 'restaurar', 'estado')]
    [string] $Accion = 'ocultar'
)

$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName System.Windows.Forms
    if (-not ('AvisoNativeOnce' -as [type])) {
        Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

public sealed class AvisoNativeTarget
{
    public IntPtr Handle;
    public uint ProcessId;
    public uint ThreadId;
    public long ProcessStart;
}

public static class AvisoNativeOnce
{
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int L,T,R,B; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder name,int size);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h,uint command);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll",SetLastError=true)] static extern bool ShowWindowAsync(IntPtr h,int command);

    static AvisoNativeTarget Validate(IntPtr h)
    {
        if(!IsWindow(h)) return null;
        StringBuilder cls=new StringBuilder(128);GetClassName(h,cls,128);
        // WorkerW is the desktop, not the watermark: never match it.
        if(cls.ToString()!="Worker Window" || GetParent(h)!=IntPtr.Zero || GetWindow(h,5)!=IntPtr.Zero) return null;
        if((GetWindowLong(h,-20)&0x80028)!=0x80028) return null;
        Rect r;if(!GetWindowRect(h,out r)) return null;
        int width=r.R-r.L,height=r.B-r.T;
        if(width<80 || width>1200 || height<15 || height>300) return null;
        bool corner=false;
        foreach(Screen screen in Screen.AllScreens) {
            System.Drawing.Rectangle b=screen.Bounds;
            if(r.L>=b.Left+b.Width/2 && r.T>=b.Top+b.Height/2 && r.R<=b.Right+8 && r.B<=b.Bottom+8) { corner=true;break; }
        }
        if(!corner) return null;
        uint pid;uint tid=GetWindowThreadProcessId(h,out pid);
        if(tid==0) return null;
        try {
            using(Process proc=Process.GetProcessById((int)pid)) {
                if(proc.SessionId!=Process.GetCurrentProcess().SessionId || !String.Equals(proc.ProcessName,"explorer",StringComparison.OrdinalIgnoreCase)) return null;
                string expected=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe");
                if(!String.Equals(proc.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase)) return null;
                return new AvisoNativeTarget {Handle=h,ProcessId=pid,ThreadId=tid,ProcessStart=proc.StartTime.ToUniversalTime().Ticks};
            }
        } catch(System.ComponentModel.Win32Exception) {return null;}
          catch(InvalidOperationException) {return null;}
          catch(ArgumentException) {return null;}
    }

    public static AvisoNativeTarget[] Find()
    {
        var result=new List<AvisoNativeTarget>();
        var seen=new HashSet<IntPtr>();
        IntPtr current=IntPtr.Zero;
        for(int i=0;i<32;i++) {
            current=FindWindowEx(IntPtr.Zero,current,"Worker Window",null);
            if(current==IntPtr.Zero || !seen.Add(current)) break;
            AvisoNativeTarget target=Validate(current);
            if(target!=null) result.Add(target);
        }
        return result.ToArray();
    }

    public static bool SetVisibility(AvisoNativeTarget target,bool visible)
    {
        AvisoNativeTarget now=Validate(target.Handle);
        if(now==null || now.ProcessId!=target.ProcessId || now.ThreadId!=target.ThreadId || now.ProcessStart!=target.ProcessStart) return false;
        return ShowWindowAsync(target.Handle,visible?8:0);
    }
}
'@
    }

    $avisoTargets = @([AvisoNativeOnce]::Find())
    if ($avisoTargets.Count -eq 0) {
        Write-Output 'No se encontro una ventana compatible del aviso. No se cambio nada.'
        exit 2
    }
    if ($Accion -eq 'estado') {
        foreach ($avisoTarget in $avisoTargets) {
            $avisoState = if ([AvisoNativeOnce]::IsWindowVisible($avisoTarget.Handle)) { 'VISIBLE' } else { 'OCULTO' }
            Write-Output ('Aviso: {0} (ventana 0x{1:X})' -f $avisoState, $avisoTarget.Handle.ToInt64())
        }
        exit 0
    }

    $avisoShow = $Accion -eq 'restaurar'
    foreach ($avisoTarget in $avisoTargets) {
        if ([AvisoNativeOnce]::IsWindowVisible($avisoTarget.Handle) -ne $avisoShow) {
            if (-not [AvisoNativeOnce]::SetVisibility($avisoTarget, $avisoShow)) {
                Write-Output 'No se pudo iniciar el cambio de visibilidad.'
                exit 3
            }
        }
    }
    # A bounded verification of the asynchronous request, not a resident monitor.
    Start-Sleep -Milliseconds 200
    foreach ($avisoTarget in $avisoTargets) {
        if ([AvisoNativeOnce]::IsWindowVisible($avisoTarget.Handle) -ne $avisoShow) {
            Write-Output 'La visibilidad no coincide con lo solicitado. Windows u otra app pueden estar mostrando u ocultando el aviso.'
            exit 4
        }
    }
    if ($avisoShow) {
        Write-Output 'Aviso restaurado. El script ha terminado.'
    } else {
        Write-Output 'Aviso oculto. El script ha terminado: no queda un proceso vigilando.'
    }
    exit 0
} catch {
    Write-Output ('Error: {0}' -f $_.Exception.Message)
    exit 1
}
