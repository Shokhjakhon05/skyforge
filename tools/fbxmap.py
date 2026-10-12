"""Reads binary FBX files and writes which picture each material uses (by connection), as JSON."""
import struct, sys, os, json, zlib

def parse(data):
    version = struct.unpack_from('<I', data, 23)[0]
    big = version >= 7500
    head = 25 if big else 13
    def node(pos):
        if big: end, nprops, plen = struct.unpack_from('<QQQ', data, pos); p = pos + 24
        else: end, nprops, plen = struct.unpack_from('<III', data, pos); p = pos + 12
        nlen = data[p]; name = data[p+1:p+1+nlen].decode('latin1'); p += 1 + nlen
        if end == 0: return None, pos + head
        props = []
        q = p
        for _ in range(nprops):
            t = chr(data[q]); q += 1
            if t in 'YCIFDL':
                size = {'Y':2,'C':1,'I':4,'F':4,'D':8,'L':8}[t]
                props.append(struct.unpack_from('<' + {'Y':'h','C':'b','I':'i','F':'f','D':'d','L':'q'}[t], data, q)[0]); q += size
            elif t in 'fdlib':
                n, enc, clen = struct.unpack_from('<III', data, q); q += 12
                props.append(None); q += clen
            elif t in 'SR':
                n = struct.unpack_from('<I', data, q)[0]; q += 4
                props.append(data[q:q+n].decode('latin1') if t == 'S' else None); q += n
            else: raise ValueError(t)
        q = p + plen
        kids = []
        while q < end:
            k, q2 = node(q)
            if k is None: break
            kids.append(k); q = q2
        return (name, props, kids), end
    pos = 27; top = []
    while pos < len(data) - head:
        n, pos2 = node(pos)
        if n is None: break
        top.append(n); pos = pos2
    return top

def find(nodes, name): return [n for n in nodes if n[0] == name]

out = {}
for path in sys.argv[2:]:
    key = os.path.splitext(os.path.basename(path))[0]
    top = parse(open(path, 'rb').read())
    objects = find(top, 'Objects')[0][2]; conns = find(top, 'Connections')[0][2]
    mats = {n[1][0]: n[1][1].split('\x00')[0] for n in find(objects, 'Material')}
    texs = {}
    for n in find(objects, 'Texture'):
        fn = [k[1][0] for k in n[2] if k[0] in ('RelativeFilename', 'FileName')]
        texs[n[1][0]] = os.path.basename(fn[0].replace('\\', '/')) if fn else ''
    m = {}
    for c in conns:
        p = c[1]
        if p[0] == 'OP' and p[1] in texs and p[2] in mats:
            m.setdefault(mats[p[2]], {})[p[3]] = texs[p[1]]
    out[key] = m
    print(key, json.dumps(m)[:600])
json.dump(out, open(sys.argv[1], 'w'), indent=1, sort_keys=True)
