using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AvisoTransparente
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int L,T,R,B; }
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetClassName(IntPtr h,StringBuilder text,int count);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h,out Rect r);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr h,uint command);
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")] internal static extern int GetWindowLong(IntPtr h,int index);
        [DllImport("user32.dll",SetLastError=true)] internal static extern bool ShowWindowAsync(IntPtr h,int cmd);
        [DllImport("user32.dll",SetLastError=true)] internal static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h,int id);
        internal delegate void WinEventProc(IntPtr hook,uint eventType,IntPtr window,int objectId,int childId,uint thread,uint time);
        [DllImport("user32.dll",SetLastError=true)] internal static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEventProc callback,uint process,uint thread,uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
    }

    internal sealed class Target
    {
        public IntPtr Handle;
        public uint ProcessId,ThreadId;
        public long ProcessStart;
        public int Session;
    }

    // The class is deliberately exact: WorkerW is the desktop and must not be touched.
    internal sealed class WatermarkController : IDisposable
    {
        private readonly Dictionary<IntPtr,Target> hidden = new Dictionary<IntPtr,Target>();
        private readonly int session = Process.GetCurrentProcess().SessionId;
        private readonly string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe");
        private IntPtr eventHook;
        private Native.WinEventProc eventCallback;
        private GCHandle callbackRoot;
        private bool disposed;
        public event Action StateChanged;
        public bool Automatic { get; set; }
        public long TargetEvents { get; private set; }
        public string EventError { get; private set; }
        public bool Active { get; private set; }
        public int ChangedCount { get { return hidden.Count; } }

        public WatermarkController() { Automatic=true; }

        public bool Listen()
        {
            if(eventHook!=IntPtr.Zero) return true;
            eventCallback=OnWindowEvent;
            callbackRoot=GCHandle.Alloc(eventCallback);
            // OS-delivered create/destroy/show/hide events. No timer and no injected DLL.
            eventHook=Native.SetWinEventHook(0x8000,0x8003,IntPtr.Zero,eventCallback,0,0,2);
            if(eventHook!=IntPtr.Zero) return true;
            callbackRoot.Free();eventCallback=null;
            EventError="No se pudieron recibir notificaciones. La ocultación manual sigue disponible.";
            return false;
        }

        private void OnWindowEvent(IntPtr hook,uint type,IntPtr h,int objectId,int childId,uint thread,uint time)
        {
            if(disposed || h==IntPtr.Zero || objectId!=0 || childId!=0) return;
            try {
                if(type==0x8001) {
                    if(hidden.Remove(h)) {TargetEvents++;Notify();}
                    return;
                }
                // Almost all desktop events are discarded before process/module inspection.
                StringBuilder cls=new StringBuilder(128);Native.GetClassName(h,cls,cls.Capacity);
                if(cls.ToString()!="Worker Window") return;
                Target target=Validate(h);
                if(target==null) return;
                TargetEvents++;
                if(Active && Automatic && (type==0x8000 || type==0x8002)) HideTarget(target);
                Notify();
            } catch(Exception e) {
                // Exceptions must never cross the native callback boundary.
                EventError="No se pudo procesar una notificación: "+e.Message;
            }
        }

        private void Notify() { Action changed=StateChanged;if(changed!=null) changed(); }

        private void HideTarget(Target target)
        {
            if(!Native.IsWindowVisible(target.Handle)) return;
            if(!hidden.ContainsKey(target.Handle)) hidden.Add(target.Handle,target);
            if(!Native.ShowWindowAsync(target.Handle,0)) EventError="Windows no aceptó la solicitud de ocultación.";
        }

        public List<Target> Find()
        {
            List<Target> result=new List<Target>();
            IntPtr cursor=IntPtr.Zero;
            HashSet<IntPtr> seen=new HashSet<IntPtr>();
            for(int i=0;i<32;i++) {
                cursor=Native.FindWindowEx(IntPtr.Zero,cursor,"Worker Window",null);
                if(cursor==IntPtr.Zero || !seen.Add(cursor)) break;
                Target target=Validate(cursor);
                if(target!=null) result.Add(target);
            }
            return result;
        }

        private Target Validate(IntPtr h)
        {
            if(!Native.IsWindow(h)) return null;
            StringBuilder cls=new StringBuilder(128); Native.GetClassName(h,cls,cls.Capacity);
            if(cls.ToString()!="Worker Window" || Native.GetParent(h)!=IntPtr.Zero || Native.GetWindow(h,5)!=IntPtr.Zero) return null;
            // A small, click-through, layered, topmost Explorer window near a screen's lower right.
            int ex=Native.GetWindowLong(h,-20);
            if((ex & 0x80028)!=0x80028) return null;
            Native.Rect rect; if(!Native.GetWindowRect(h,out rect)) return null;
            int width=rect.R-rect.L,height=rect.B-rect.T;
            if(width<80 || height<15 || width>1200 || height>300) return null;
            bool corner=false;
            foreach(Screen screen in Screen.AllScreens) {
                Rectangle b=screen.Bounds;
                if(rect.L>=b.Left+b.Width/2 && rect.T>=b.Top+b.Height/2 && rect.R<=b.Right+8 && rect.B<=b.Bottom+8) { corner=true;break; }
            }
            if(!corner) return null;
            uint pid;uint tid=Native.GetWindowThreadProcessId(h,out pid);
            try {
                using(Process proc=Process.GetProcessById((int)pid)) {
                    if(proc.SessionId!=session || !String.Equals(proc.ProcessName,"explorer",StringComparison.OrdinalIgnoreCase)) return null;
                    if(!String.Equals(proc.MainModule.FileName,explorer,StringComparison.OrdinalIgnoreCase)) return null;
                    return new Target { Handle=h, ProcessId=pid,ThreadId=tid, ProcessStart=proc.StartTime.ToUniversalTime().Ticks,Session=session };
                }
            } catch(System.ComponentModel.Win32Exception) { return null; }
              catch(InvalidOperationException) { return null; }
              catch(ArgumentException) { return null; }
        }

        private bool Same(Target target)
        {
            Target now=Validate(target.Handle);
            return now!=null && now.ProcessId==target.ProcessId && now.ThreadId==target.ThreadId && now.ProcessStart==target.ProcessStart;
        }

        public bool Start()
        {
            List<Target> targets=Find();
            if(targets.Count==0) return false;
            Active=true;
            KeepHidden();
            return true;
        }

        public void KeepHidden()
        {
            if(!Active) return;
            List<IntPtr> stale=new List<IntPtr>();
            foreach(KeyValuePair<IntPtr,Target> pair in hidden) if(!Same(pair.Value)) stale.Add(pair.Key);
            foreach(IntPtr h in stale) hidden.Remove(h);
            foreach(Target target in Find()) HideTarget(target);
        }

        public void Restore(bool includeDetected=false)
        {
            Active=false;
            var targets=new Dictionary<IntPtr,Target>(hidden);
            if(includeDetected) foreach(Target target in Find()) targets[target.Handle]=target;
            foreach(Target target in targets.Values) if(Same(target)) Native.ShowWindowAsync(target.Handle,8); // show without activating
            hidden.Clear();
        }

        public void LeaveHidden() {Active=false;hidden.Clear();}

        public bool AllHidden()
        {
            List<Target> targets=Find();
            if(targets.Count==0) return false;
            foreach(Target target in targets) if(Native.IsWindowVisible(target.Handle)) return false;
            return true;
        }

        public string Status(out string detail)
        {
            List<Target> targets=Find();
            if(targets.Count==0) {
                detail=Active ? "Esperando a que Windows vuelva a crear el aviso." : "No se encontró una ventana compatible. No se ha cambiado nada.";
                return "No detectado";
            }
            int visible=0;foreach(Target t in targets) if(Native.IsWindowVisible(t.Handle)) visible++;
            if(Active && visible==0) {
                detail="La ventana del aviso está oculta. El contenido de fondo sigue visible.";
                return "Aviso oculto";
            }
            if(Active && visible>0) {
                detail="Windows volvió a mostrar el aviso. Puedes intentar ocultarlo otra vez.";
                return "Aviso visible";
            }
            detail=visible>0 ? "Se encontró el aviso. Puedes ocultarlo sin reiniciar Windows." : "La ventana del aviso ya está oculta por otro motivo.";
            return visible>0 ? "Aviso detectado" : "Ya está oculto";
        }

        public void Dispose()
        {
            if(disposed) return;
            disposed=true;
            if(eventHook!=IntPtr.Zero) {Native.UnhookWinEvent(eventHook);eventHook=IntPtr.Zero;}
            if(callbackRoot.IsAllocated) callbackRoot.Free();
            eventCallback=null;StateChanged=null;Restore();
        }
    }

    internal sealed class HotkeyChoice
    {
        public uint Modifiers=3;
        public uint Key=0x78;
        public bool IsValid()
        {
            return Modifiers>=1 && Modifiers<=7 &&
                ((Key>=0x41 && Key<=0x5A) || (Key>=0x30 && Key<=0x39) ||
                 (Key>=0x70 && Key<=0x87 && Key!=0x7B));
        }
        public bool Same(HotkeyChoice other) {return other!=null && other.Modifiers==Modifiers && other.Key==Key;}
        public string KeyName {get {return Key>=0x70 && Key<=0x87?"F"+(Key-0x6F).ToString():((char)Key).ToString();}}
        public override string ToString()
        {
            string text="";
            if((Modifiers&2)!=0) text+="Ctrl + ";
            if((Modifiers&1)!=0) text+="Alt + ";
            if((Modifiers&4)!=0) text+="Shift + ";
            return text+KeyName;
        }
    }

    internal static class HotkeySettings
    {
        public static string DefaultPath {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AvisoTransparente","hotkey.ini");}}
        public static HotkeyChoice Load(string path)
        {
            try {
                uint mods=0,key=0;
                foreach(string line in File.ReadAllLines(path)) {
                    if(line.StartsWith("modifiers=")) uint.TryParse(line.Substring(10),out mods);
                    if(line.StartsWith("key=")) uint.TryParse(line.Substring(4),out key);
                }
                HotkeyChoice value=new HotkeyChoice {Modifiers=mods,Key=key};
                if(value.IsValid()) return value;
            } catch(IOException) {} catch(UnauthorizedAccessException) {}
            return new HotkeyChoice();
        }
        public static void Save(string path,HotkeyChoice value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                File.WriteAllText(temporary,"modifiers="+value.Modifiers+Environment.NewLine+"key="+value.Key+Environment.NewLine,Encoding.UTF8);
                if(File.Exists(path)) File.Replace(temporary,path,null);
                else File.Move(temporary,path);
            } finally {if(File.Exists(temporary)) File.Delete(temporary);}
        }
    }

    internal sealed class HotkeyBinding
    {
        public int Id {get;private set;}
        public HotkeyChoice Current {get;private set;}
        public bool TryApply(IntPtr window,HotkeyChoice choice,string path,bool save,out string error)
        {
            error=null;
            if(!choice.IsValid()) {error="Elige Ctrl, Alt o Shift y una tecla. F12 está reservada por Windows.";return false;}
            bool unchanged=Id!=0 && choice.Same(Current);
            int nextId=Id==1?2:1;
            if(!unchanged && !Native.RegisterHotKey(window,nextId,choice.Modifiers|0x4000,choice.Key)) {
                error="Ese atajo no está disponible. Elige otra combinación.";return false;
            }
            try {if(save) HotkeySettings.Save(path,choice);}
            catch(IOException) {error="No se pudo guardar el atajo. Se conserva el anterior.";}
            catch(UnauthorizedAccessException) {error="No hay permiso para guardar el atajo. Se conserva el anterior.";}
            if(error!=null) {if(!unchanged) Native.UnregisterHotKey(window,nextId);return false;}
            if(!unchanged) {Release(window);Id=nextId;Current=new HotkeyChoice {Modifiers=choice.Modifiers,Key=choice.Key};}
            return true;
        }
        public void Release(IntPtr window) {if(Id!=0) Native.UnregisterHotKey(window,Id);Id=0;}
    }

    internal sealed class KeyOption
    {
        public uint Key;
        public override string ToString() {return new HotkeyChoice {Key=Key}.KeyName;}
    }

    internal sealed class MainForm : Form
    {
        readonly WatermarkController controller=new WatermarkController();
        readonly Label state=new Label(), detail=new Label(),shortcut=new Label();
        readonly Button hide=new Button(), restore=new Button();
        readonly CheckBox maintain=new CheckBox();
        readonly NotifyIcon tray=new NotifyIcon();
        readonly ToolStripMenuItem trayToggle=new ToolStripMenuItem();
        readonly CheckBox ctrl=new CheckBox(),alt=new CheckBox(),shift=new CheckBox();
        readonly ComboBox keyChoice=new ComboBox();
        readonly HotkeyBinding hotkey=new HotkeyBinding();
        readonly string settingsPath;
        HotkeyChoice requestedHotkey;
        bool stateRefreshPending,closing;
        static readonly Color Ink=Color.FromArgb(26,40,43), Muted=Color.FromArgb(73,90,94), Accent=Color.FromArgb(0,100,94);

        public MainForm() : this(HotkeySettings.DefaultPath) {}
        internal MainForm(string configurationPath)
        {
            settingsPath=configurationPath;requestedHotkey=HotkeySettings.Load(settingsPath);
            Text="Aviso Transparente";
            Font=new Font("Segoe UI",10F);
            BackColor=Color.FromArgb(248,249,246); ForeColor=Ink;
            AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96F,96F);
            ClientSize=new Size(620,490); FormBorderStyle=FormBorderStyle.FixedSingle;
            MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
            Icon=SystemIcons.Application;

            Label title=LabelAt("Ocultar el aviso de Windows",28,24,564,40,21F,FontStyle.Bold);
            Controls.Add(title);
            Panel statusPanel=new Panel { Location=new Point(28,88),Size=new Size(564,94),BackColor=Color.FromArgb(234,241,237) };
            state.Location=new Point(18,16);state.Size=new Size(528,28);state.Font=new Font(Font,FontStyle.Bold);state.ForeColor=Accent;
            detail.Location=new Point(18,52);detail.Size=new Size(528,32);detail.ForeColor=Muted;
            statusPanel.Controls.Add(state);statusPanel.Controls.Add(detail);Controls.Add(statusPanel);

            hide.Text="&Ocultar aviso";hide.Location=new Point(28,200);hide.Size=new Size(274,48);
            hide.BackColor=Accent;hide.ForeColor=Color.White;hide.FlatStyle=FlatStyle.Flat;hide.FlatAppearance.BorderSize=0;
            hide.Click+=delegate { EnableHiding(); };
            restore.Text="&Restaurar aviso";restore.Location=new Point(318,200);restore.Size=new Size(274,48);
            restore.Click+=delegate { controller.Restore(true); RefreshState(); };
            Controls.Add(hide);Controls.Add(restore);AcceptButton=hide;

            maintain.Text="&Mantener oculto si Windows lo vuelve a mostrar";
            maintain.Location=new Point(28,266);maintain.Size=new Size(564,28);maintain.Checked=true;
            maintain.CheckedChanged+=delegate {
                controller.Automatic=maintain.Checked;
                if(controller.Active && maintain.Checked) controller.KeepHidden();
                RefreshState();
            };
            Controls.Add(maintain);
            Controls.Add(LabelAt("Atajo de teclado",28,310,564,24,10F,FontStyle.Bold));
            ctrl.Text="Ctrl";ctrl.Location=new Point(28,340);ctrl.Size=new Size(76,32);ctrl.Checked=(requestedHotkey.Modifiers&2)!=0;
            alt.Text="Alt";alt.Location=new Point(112,340);alt.Size=new Size(65,32);alt.Checked=(requestedHotkey.Modifiers&1)!=0;
            shift.Text="Shift";shift.Location=new Point(185,340);shift.Size=new Size(76,32);shift.Checked=(requestedHotkey.Modifiers&4)!=0;
            Controls.Add(ctrl);Controls.Add(alt);Controls.Add(shift);
            keyChoice.Location=new Point(274,343);keyChoice.Size=new Size(114,32);keyChoice.DropDownStyle=ComboBoxStyle.DropDownList;
            keyChoice.AccessibleName="Tecla del atajo";
            for(uint k=0x70;k<=0x87;k++) if(k!=0x7B) keyChoice.Items.Add(new KeyOption {Key=k});
            for(uint k=0x41;k<=0x5A;k++) keyChoice.Items.Add(new KeyOption {Key=k});
            for(uint k=0x30;k<=0x39;k++) keyChoice.Items.Add(new KeyOption {Key=k});
            foreach(KeyOption option in keyChoice.Items) if(option.Key==requestedHotkey.Key) {keyChoice.SelectedItem=option;break;}
            Controls.Add(keyChoice);
            Button applyHotkey=new Button {Text="&Guardar atajo",Location=new Point(414,338),Size=new Size(178,36)};
            applyHotkey.Click+=delegate {ApplyHotkey();};Controls.Add(applyHotkey);
            shortcut.Location=new Point(28,382);shortcut.Size=new Size(564,36);shortcut.ForeColor=Muted;
            Controls.Add(shortcut);

            Button minimize=new Button { Text="Dejar en &bandeja",Location=new Point(28,432),Size=new Size(274,40) };
            minimize.Click+=delegate { Hide(); tray.ShowBalloonTip(2500,"Aviso Transparente","Doble clic en el icono para abrir los controles.",ToolTipIcon.Info); };
            Controls.Add(minimize);
            Button once=new Button { Text="Ocultar y &salir",Location=new Point(318,432),Size=new Size(274,40) };
            once.Click+=delegate { HideAndExit(); };Controls.Add(once);

            ContextMenuStrip menu=new ContextMenuStrip();
            menu.Items.Add("Abrir controles",null,delegate { OpenControls(); });
            trayToggle.Click+=delegate { Toggle(); };menu.Items.Add(trayToggle);
            menu.Items.Add("Salir y restaurar",null,delegate { Close(); });
            tray.Icon=Icon;tray.Text="Aviso Transparente";tray.ContextMenuStrip=menu;tray.Visible=true;
            tray.DoubleClick+=delegate { OpenControls(); };

            controller.StateChanged+=QueueRefresh;
            if(!controller.Listen()) {maintain.Checked=false;maintain.Enabled=false;}
            Activated+=delegate {RefreshState();};
            VisibleChanged+=delegate {if(Visible && IsHandleCreated) RefreshState();};
            RefreshState();
        }

        Label LabelAt(string text,int x,int y,int w,int h,float fontSize,FontStyle style)
        {
            return new Label { Text=text,Location=new Point(x,y),Size=new Size(w,h),Font=new Font("Segoe UI",fontSize,style),ForeColor=style==FontStyle.Bold?Ink:Muted };
        }
        void EnableHiding()
        {
            if(!controller.Start()) {
                state.Text="No detectado";detail.Text="No se identificó el aviso de forma segura. No se cambió ninguna ventana.";
                return;
            }
            RefreshState();
        }
        void Toggle() { if(controller.Active) controller.Restore(true); else EnableHiding(); RefreshState(); }
        async void HideAndExit()
        {
            if(!controller.Start()) {EnableHiding();return;}
            // One bounded wait to verify the queued native request; never a recurring check.
            await Task.Delay(200);
            if(closing || IsDisposed) return;
            if(!controller.AllHidden()) {detail.Text="Windows no ocultó el aviso. Puedes volver a intentarlo.";return;}
            controller.LeaveHidden();Close();
        }
        void QueueRefresh()
        {
            if(closing || IsDisposed || !IsHandleCreated || stateRefreshPending) return;
            stateRefreshPending=true;
            BeginInvoke((Action)delegate {stateRefreshPending=false;if(!closing && !IsDisposed) RefreshState();});
        }
        void OpenControls() { Show(); WindowState=FormWindowState.Normal;Activate(); }
        void ApplyHotkey()
        {
            KeyOption selected=keyChoice.SelectedItem as KeyOption;
            if(selected==null) {shortcut.Text="Selecciona una tecla.";return;}
            uint modifiers=(ctrl.Checked?2u:0u)|(alt.Checked?1u:0u)|(shift.Checked?4u:0u);
            HotkeyChoice choice=new HotkeyChoice {Modifiers=modifiers,Key=selected.Key};
            string error;
            if(hotkey.TryApply(Handle,choice,settingsPath,true,out error)) {
                requestedHotkey=choice;shortcut.ForeColor=Muted;shortcut.Text="Guardado: "+choice;
            } else {shortcut.ForeColor=Color.FromArgb(151,42,33);shortcut.Text=error;}
        }
        void RefreshState()
        {
            string description;state.Text=controller.Status(out description);detail.Text=description;
            hide.Enabled=!controller.Active || state.Text=="Aviso visible";
            restore.Enabled=state.Text!="No detectado" || controller.ChangedCount>0;
            if(!String.IsNullOrEmpty(controller.EventError)) detail.Text=controller.EventError;
            trayToggle.Text=controller.Active?"Restaurar aviso":"Ocultar aviso";
            tray.Text=controller.Active?"Aviso Transparente: ocultación activa":"Aviso Transparente: inactivo";
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            string error;
            if(hotkey.TryApply(Handle,requestedHotkey,settingsPath,false,out error)) shortcut.Text="Activo: "+requestedHotkey;
            else {shortcut.ForeColor=Color.FromArgb(151,42,33);shortcut.Text=error;}
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            hotkey.Release(Handle);base.OnHandleDestroyed(e);
        }
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x312 && hotkey.Id!=0 && m.WParam.ToInt32()==hotkey.Id) Toggle();
            base.WndProc(ref m);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closing=true;controller.Dispose();tray.Visible=false;
            hotkey.Release(Handle);
            tray.Dispose();base.OnFormClosed(e);
        }
    }

    internal static class Program
    {
        [STAThread] static int Main(string[] args)
        {
            if(args.Length>0 && args[0]=="--diagnose") {
                using(WatermarkController c=new WatermarkController()) {
                    List<Target> targets=c.Find();
                    string detail;string status=c.Status(out detail);
                    string report="CompatibleWindows="+targets.Count+Environment.NewLine+"Status="+status+Environment.NewLine+detail+Environment.NewLine;
                    foreach(Target t in targets) report+="Handle=0x"+t.Handle.ToInt64().ToString("X")+" Visible="+Native.IsWindowVisible(t.Handle)+Environment.NewLine;
                    if(args.Length>1) File.WriteAllText(args[1],report,Encoding.UTF8);
                    return targets.Count>0?0:2;
                }
            }
            bool created;
            using(Mutex instance=new Mutex(true,"Local\\AvisoTransparente.UserSession.v1",out created)) {
                if(!created) { MessageBox.Show("La app ya está abierta. Busca su icono en la bandeja junto al reloj.","Aviso Transparente");return 0; }
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException+=delegate(object sender,ThreadExceptionEventArgs e) {
                    MessageBox.Show("Se produjo un error. Cierra la app para restaurar el aviso.\n\n"+e.Exception.Message,"Aviso Transparente",MessageBoxButtons.OK,MessageBoxIcon.Error);
                    Application.Exit();
                };
                using(MainForm form=new MainForm()) Application.Run(form);
                instance.ReleaseMutex();
            }
            return 0;
        }
    }
}
