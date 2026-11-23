import os,subprocess,glob
def build(D=r'E:/Project/NOModding/F14BDebugPlugin', out='F14B.FlightDebug.dll'):
    G=r'D:/Steam/steamapps/common/Nuclear Option'
    M=G+'/NuclearOption_Data/Managed'
    refs=glob.glob(M+'/*.dll')+[G+'/BepInEx/core/BepInEx.dll',G+'/BepInEx/core/0Harmony.dll']
    U=r'C:/Program Files/Unity/Hub/Editor/2022.3.62f2/Editor/Data'
    rsp=D+'/build.rsp'
    with open(rsp,'w') as f:
        f.write('-nologo\n-target:library\n-langversion:latest\n-optimize+\n-nostdlib+\n-noconfig\n-deterministic\n-nowarn:CS0649,CS0169,CS0414,CS1701,CS1702\n')
        f.write('-out:"%s"\n' % (D+'/'+out))
        for r in refs: f.write('-r:"%s"\n' % r)
        f.write('"%s"\n' % (D+'/FlightDebug.cs'))
    if os.path.exists(D+'/'+out): os.remove(D+'/'+out)
    p=subprocess.run([U+'/NetCoreRuntime/dotnet.exe',U+'/DotNetSdkRoslyn/csc.dll','@'+rsp],capture_output=True,text=True,timeout=70)
    o=p.stdout+p.stderr
    errs=[l for l in o.splitlines() if ': error' in l]
    return {'rc':p.returncode,'errors':errs[:40],'n_err':len(errs),'warn':[l for l in o.splitlines() if ': warning' in l][:10],'dll':os.path.getsize(D+'/'+out) if os.path.exists(D+'/'+out) else 0}
