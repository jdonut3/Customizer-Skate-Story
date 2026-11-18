"""Make a .smp patch: the changes from an original game file to the changed file.
The patch holds only the new bytes plus 'copy this range from your own file' steps."""
import sys,struct,hashlib,gzip,numpy as np
K=32
def make(orig,new,rel,out):
    idx={}
    for o in range(0,len(orig)-K,4): idx.setdefault(orig[o:o+K],o)
    a=np.frombuffer(orig,np.uint8); b=np.frombuffer(new,np.uint8)
    ops=[]; lit=bytearray(); p=0; n=len(new); copied=0
    def flush():
        nonlocal lit
        if lit: ops.append(b'\x01'+struct.pack('<i',len(lit))+bytes(lit)); lit=bytearray()
    while p<n:
        o=idx.get(new[p:p+K]) if p+K<=n else None
        if o is None:
            step=min(4,n-p); lit+=new[p:p+step]; p+=step; continue
        m=min(len(orig)-o,n-p); d=np.nonzero(a[o:o+m]!=b[p:p+m])[0]; L=int(d[0]) if len(d) else m
        # extend backward into the pending literal bytes
        back=0
        while back<len(lit) and o-back-1>=0 and orig[o-back-1]==lit[len(lit)-back-1]: back+=1
        if back: del lit[len(lit)-back:]
        flush(); ops.append(b'\x00'+struct.pack('<qi',o-back,L+back)); copied+=L+back; p+=L
    flush(); ops.append(b'\x02')
    body=gzip.compress(b''.join(ops),9)
    r=rel.encode()
    open(out,'wb').write(b'SMP1'+struct.pack('<i',len(r))+r+struct.pack('<q',len(orig))+hashlib.sha256(orig).digest()+struct.pack('<q',len(new))+hashlib.sha256(new).digest()+body)
    return copied,len(body)
def apply(orig,patch):
    assert patch[:4]==b'SMP1'; n=struct.unpack_from('<i',patch,4)[0]; q=8+n; rel=patch[8:q].decode()
    ol=struct.unpack_from('<q',patch,q)[0]; osha=patch[q+8:q+40]; nl=struct.unpack_from('<q',patch,q+40)[0]; nsha=patch[q+48:q+80]
    assert len(orig)==ol and hashlib.sha256(orig).digest()==osha
    ops=gzip.decompress(patch[q+80:]); out=bytearray(); i=0
    while True:
        t=ops[i]; i+=1
        if t==0: o,L=struct.unpack_from('<qi',ops,i); i+=12; out+=orig[o:o+L]
        elif t==1: L=struct.unpack_from('<i',ops,i)[0]; i+=4; out+=ops[i:i+L]; i+=L
        else: break
    assert len(out)==nl and hashlib.sha256(bytes(out)).digest()==nsha; return rel,bytes(out)
if __name__=='__main__':
    D='/mnt/user-data/uploads/SkateStory_Data/StreamingAssets/aa/StandaloneWindows64/'
    import os; os.makedirs('pkg/ShoeMod/patches',exist_ok=True)
    for f,src in(('71e5e8adb29c34fc67055940f70bebef.bundle','../out6/'),('27a60a716d4f849e5bcb9d18b7d2322a.bundle','../out6/'),('29f7529af32fd767d5d46b3a4dfef808.bundle','../out5/')):
        orig=open(D+f,'rb').read(); new=open(src+f,'rb').read(); out='pkg/ShoeMod/patches/'+f[:8]+'.smp'
        c,sz=make(orig,new,'SkateStory_Data/StreamingAssets/aa/StandaloneWindows64/'+f,out)
        rel,chk=apply(orig,open(out,'rb').read()); print(f[:8],'orig',len(orig),'new',len(new),'copied from orig',c,'patch',os.path.getsize(out),'verify',chk==new,rel[-30:])
