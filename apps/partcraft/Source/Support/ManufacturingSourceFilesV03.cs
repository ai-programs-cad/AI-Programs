using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Collections.Generic;
public static class ManufacturingSourceFilesV03 {
    public static readonly List<object> Operations=new List<object>();
    static string Digest(Stream stream){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
    public static object Error(Exception ex,string operation,string stage,string path){return new{operation,stage,path,hresult="0x"+ex.HResult.ToString("X8"),exception=ex.GetType().FullName,message=ex.Message,stackTrace=ex.StackTrace,details=ex.ToString()};}
    static FileStream Read(string path){return new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);}
    public static string Hash(string path,string stage){
        const string op="SOURCE_SHARED_READ_SHA256_TWO_PASS";
        try{
            string first,second;long length;
            using(var stream=Read(path)){length=stream.Length;first=Digest(stream);stream.Position=0;second=Digest(stream);if(length!=stream.Length||first!=second)throw new IOException("SOURCE_CHANGED_DURING_SHARED_READ");}
            Operations.Add(new{operation=op,stage,path,success=true,share="ReadWrite|Delete",bytes=length,sha256=first,atomicSnapshotGuaranteed=false});return first;
        }catch(Exception ex){Operations.Add(Error(ex,op,stage,path));ex.Data["operation"]=op;ex.Data["stage"]=stage;ex.Data["path"]=path;throw;}
    }
    public static bool TryHash(string path,string stage,out string hash){try{hash=Hash(path,stage);return true;}catch{hash=null;return false;}}
    public static void CopySavedSource(string source,string destination,string expectedHash,bool dirty){
        const string op="COPY_SAVED_SOURCE_SHARED_STREAM_CREATE_NEW";string stage="CREATE_OWNED_COPY";
        try{
            if(dirty)throw new InvalidOperationException("DIRTY_SOURCE_COPY_FORBIDDEN: filesystem copy cannot preserve unsaved COM geometry; source will not be saved");
            if(String.IsNullOrEmpty(expectedHash))throw new InvalidOperationException("SOURCE_HASH_REQUIRED_FOR_COPY");
            if(ManufacturingContextV02.SamePath(source,destination))throw new InvalidOperationException("SOURCE_DESTINATION_IDENTITY_FORBIDDEN");
            if(Hash(source,"BEFORE_COPY")!=expectedHash)throw new IOException("SOURCE_DISK_CHANGED_BEFORE_COPY");
            using(var input=Read(source))using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None)){input.CopyTo(output);output.Flush(true);}
            if(Hash(destination,"VERIFY_OWNED_COPY")!=expectedHash||Hash(source,"AFTER_COPY")!=expectedHash)throw new IOException("SOURCE_OR_COPY_HASH_CHANGED; copy must not be opened");
            Operations.Add(new{operation=op,stage,path=source,destination,success=true,sourceSaved=false});
        }catch(Exception ex){Operations.Add(Error(ex,op,stage,source));ex.Data["operation"]=op;ex.Data["stage"]=stage;ex.Data["path"]=source;throw;}
    }
    public static bool StateEqual(object before,object after){return CutListCoreV01.Json.Serialize(before)==CutListCoreV01.Json.Serialize(after);}
}
