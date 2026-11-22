"""Enable lo only in a new private Linux network namespace."""
import errno
import fcntl
import os
from pathlib import Path
import socket
import struct
import sys

interfaces={i.split(":")[0].strip() for i in Path("/proc/net/dev").read_text().splitlines()[2:]}
assert interfaces=={"lo"}, "Refusing to alter a network namespace with other interfaces"
with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as fd:
    req=bytearray(fcntl.ioctl(fd,0x8913,struct.pack("16sh22x",b"lo",0)))
    old=struct.unpack_from("h",req,16)[0]
    assert not old&1, "Refusing to alter existing configured loopback"
    struct.pack_into("h",req,16,old|1)
    fcntl.ioctl(fd,0x8914,req)
with socket.socket(socket.AF_INET,socket.SOCK_STREAM) as fd:
    fd.settimeout(1)
    assert fd.connect_ex(("192.0.2.1",443))==errno.ENETUNREACH
print("Runtime namespace has loopback only; nonloopback destination unreachable.",flush=True)
os.execvp(sys.argv[1],sys.argv[1:])
