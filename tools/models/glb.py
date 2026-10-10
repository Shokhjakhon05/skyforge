"""Minimal GLB reader: flattens the scene into triangles with a colour each."""
import io, json, math, struct

COMP = {5120: ('b', 1), 5121: ('B', 1), 5122: ('h', 2), 5123: ('H', 2), 5125: ('I', 4), 5126: ('f', 4)}
NUM = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}


def mat_mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def node_matrix(node):
    if 'matrix' in node:
        m = node['matrix']
        return [[m[c * 4 + r] for c in range(4)] for r in range(4)]
    t = node.get('translation', [0, 0, 0]); q = node.get('rotation', [0, 0, 0, 1]); s = node.get('scale', [1, 1, 1])
    x, y, z, w = q
    r = [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
         [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
         [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]
    return [[r[i][0] * s[0], r[i][1] * s[1], r[i][2] * s[2], t[i]] for i in range(3)] + [[0, 0, 0, 1]]


class Glb:
    def __init__(self, path):
        data = open(path, 'rb').read()
        length = struct.unpack('<I', data[12:16])[0]
        self.json = json.loads(data[20:20 + length])
        start = 20 + length
        self.bin = data[start + 8:start + 8 + struct.unpack('<I', data[start:start + 4])[0]] if start < len(data) else b''
        self.images = {}

    def accessor(self, index):
        acc = self.json['accessors'][index]
        view = self.json['bufferViews'][acc['bufferView']]
        fmt, size = COMP[acc['componentType']]
        n = NUM[acc['type']]
        stride = view.get('byteStride') or size * n
        base = view.get('byteOffset', 0) + acc.get('byteOffset', 0)
        out = []
        for i in range(acc['count']):
            vals = struct.unpack_from('<' + fmt * n, self.bin, base + i * stride)
            if acc.get('normalized'):
                top = {5121: 255.0, 5123: 65535.0}.get(acc['componentType'], 1.0)
                vals = tuple(v / top for v in vals)
            out.append(vals if n > 1 else vals[0])
        return out

    def image(self, index):
        if index not in self.images:
            from PIL import Image
            img = self.json['images'][index]
            raw = None
            if 'bufferView' in img:
                view = self.json['bufferViews'][img['bufferView']]
                raw = self.bin[view.get('byteOffset', 0):view.get('byteOffset', 0) + view['byteLength']]
            elif img.get('uri', '').startswith('data:'):
                import base64
                raw = base64.b64decode(img['uri'].split(',', 1)[1])
            self.images[index] = Image.open(io.BytesIO(raw)).convert('RGB') if raw else None
        return self.images[index]

    def triangles(self):
        """Yields (node name, material name, [p0,p1,p2], rgb 0..1 linear-ish sRGB)."""
        g = self.json
        scene = g['scenes'][g.get('scene', 0)]
        identity = [[1 if i == j else 0 for j in range(4)] for i in range(4)]
        stack = [(n, identity, '') for n in scene['nodes']]
        while stack:
            index, parent, path = stack.pop()
            node = g['nodes'][index]
            world = mat_mul(parent, node_matrix(node))
            name = path + '/' + node.get('name', str(index))
            for child in node.get('children', []):
                stack.append((child, world, name))
            if 'mesh' not in node:
                continue
            # A skinned mesh is placed by its joints, not its node; the bind pose is used as is.
            m = identity if 'skin' in node else world
            for prim in g['meshes'][node['mesh']]['primitives']:
                if prim.get('mode', 4) != 4:
                    continue
                pos = self.accessor(prim['attributes']['POSITION'])
                pos = [tuple(m[r][0] * p[0] + m[r][1] * p[1] + m[r][2] * p[2] + m[r][3] for r in range(3)) for p in pos]
                uv = self.accessor(prim['attributes']['TEXCOORD_0']) if 'TEXCOORD_0' in prim['attributes'] else None
                col = self.accessor(prim['attributes']['COLOR_0']) if 'COLOR_0' in prim['attributes'] else None
                idx = self.accessor(prim['indices']) if 'indices' in prim else list(range(len(pos)))
                mat = g['materials'][prim['material']] if 'material' in prim else {}
                pbr = mat.get('pbrMetallicRoughness', {})
                factor = pbr.get('baseColorFactor', [1, 1, 1, 1])
                # glTF factors are linear; the game's palette is sRGB.
                base = tuple(pow(max(0.0, c), 1 / 2.2) for c in factor[:3])
                tex = None
                if 'baseColorTexture' in pbr:
                    tex = self.image(g['textures'][pbr['baseColorTexture']['index']]['source'])
                for t in range(0, len(idx) - 2, 3):
                    a, b, c = idx[t], idx[t + 1], idx[t + 2]
                    rgb = base
                    if tex is not None and uv is not None and len(uv) > max(a, b, c):
                        u = (uv[a][0] + uv[b][0] + uv[c][0]) / 3 % 1.0
                        v = (uv[a][1] + uv[b][1] + uv[c][1]) / 3 % 1.0
                        px = tex.getpixel((min(tex.width - 1, int(u * tex.width)), min(tex.height - 1, int(v * tex.height))))
                        rgb = tuple(base[k] * px[k] / 255.0 for k in range(3))
                    elif col is not None:
                        rgb = tuple(pow(max(0.0, (col[a][k] + col[b][k] + col[c][k]) / 3), 1 / 2.2) for k in range(3))
                    yield name, mat.get('name', ''), [pos[a], pos[b], pos[c]], rgb
