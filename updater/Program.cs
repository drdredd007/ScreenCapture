using System.Diagnostics;
using Moments;

internal static class Program
{
    [STAThread]
    static int Main(string[] args){
        if(args.Length!=1)return 2;
        string job=Path.GetDirectoryName(Path.GetFullPath(args[0]))!;UpdatePlan? plan=null;bool parentExited=false,owned=false;
        Mutex? mutex=null;
        void Acquire(){mutex=new Mutex(false,@"Local\MomentsStudio_FMoments");try{owned=mutex.WaitOne(TimeSpan.FromSeconds(10));}catch(AbandonedMutexException){owned=true;}
            if(!owned)throw new IOException("Another Moments instance is still running. Close it and try again.");}
        // Close the handle before launching: Moments checks createdNew, not
        // just mutex ownership, to distinguish its first application instance.
        void Release(){if(owned){mutex!.ReleaseMutex();owned=false;}mutex?.Dispose();mutex=null;}
        Process Start(string flag,string result){
            var si=new ProcessStartInfo(Path.Combine(plan!.Target,"Moments.exe")){UseShellExecute=false,WorkingDirectory=plan.Target};
            si.ArgumentList.Add(flag);si.ArgumentList.Add(result);return Process.Start(si)??throw new IOException("Could not restart Moments.");
        }
        try{
            plan=UpdatePackage.Read(args[0]);UpdatePackage.Validate(plan);
            using var parent=Process.GetProcessById(plan.ParentId);
            if(parent.StartTime.ToUniversalTime().Ticks!=plan.ParentStarted)throw new IOException("The original Moments process has changed.");
            File.WriteAllText(Path.Combine(job,"ready"),"");var wait=Stopwatch.StartNew();
            while(!File.Exists(Path.Combine(job,"proceed"))){
                if(File.Exists(Path.Combine(job,"cancel"))||parent.HasExited||wait.Elapsed>TimeSpan.FromSeconds(30))return 3;
                Thread.Sleep(100);
            }
            while(!parent.WaitForExit(100)){
                if(File.Exists(Path.Combine(job,"cancel"))||wait.Elapsed>TimeSpan.FromSeconds(90))throw new IOException("Moments did not close. No files were replaced.");
            }
            parentExited=true;Acquire();
            UpdatePackage.Install(plan,()=>{
                Release();string ack=Path.Combine(job,"started");Process? app=null;
                try{
                    app=Start("--updated",ack);
                    var startup=Stopwatch.StartNew();
                    while(!File.Exists(ack)){
                        if(app.HasExited)throw new IOException("The updated application exited before startup completed.");
                        if(startup.Elapsed>TimeSpan.FromSeconds(45))throw new IOException("The updated application did not finish starting.");
                        Thread.Sleep(100);
                    }
                }catch{if(app!=null && !app.HasExited){app.Kill(true);app.WaitForExit();}Acquire();throw;}
                finally{app?.Dispose();}
            });
            File.WriteAllText(Path.Combine(job,"complete"),"");return 0;
        }catch(Exception ex){
            string error=Path.Combine(job,"error.txt");File.WriteAllText(error,ex.ToString());Release();
            if(parentExited && plan!=null && ex is not AggregateException){try{using var app=Start("--update-failed",error);return 1;}catch{}}
            // Preparation errors are displayed by the still-running application.
            if(parentExited)System.Windows.Forms.MessageBox.Show(ex.Message+"\n\nDetails: "+error,"Moments update");
            return 1;
        }finally{Release();}
    }
}
