"""
Junta las exportaciones de Meshy en un solo .glb para el juego:

  - la malla con esqueleto y pesos (de cualquiera de los .glb "…_Animation_X_withSkin"),
  - todas sus animaciones (una por archivo; se descartan los restos ".001" de 0,08 s),
  - la textura de color del modelo texturizado ("…_texture.glb"), que debe ser la misma malla
    (mismo orden de vértices y mismas UV: lo comprueba).

Se queda solo con lo que el juego usa (posición, normal, UV, huesos, pesos y la textura de color);
tangentes, mapas de normales y de rugosidad se descartan. raylib no lee JPEG, así que la textura se
convierte a PNG de 1024 px (hace falta Pillow: pip install pillow).

Uso:
  python tools/merge_meshy.py salida.glb texturizado.glb anim1.glb [anim2.glb ...]
"""
import io
import json
import struct
import sys

COMPONENTS = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}
SIZES = {5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4}


def load(path):
    data = open(path, 'rb').read()
    n = struct.unpack_from('<I', data, 12)[0]
    doc = json.loads(data[20:20 + n])
    binary = b''
    if len(data) > 20 + n:
        size = struct.unpack_from('<I', data, 20 + n)[0]
        binary = data[28 + n:28 + n + size]
    return doc, binary


def accessor_bytes(doc, binary, index):
    a = doc['accessors'][index]
    view = doc['bufferViews'][a['bufferView']]
    elem = COMPONENTS[a['type']] * SIZES[a['componentType']]
    stride = view.get('byteStride', elem)
    start = view.get('byteOffset', 0) + a.get('byteOffset', 0)
    if stride == elem:
        return binary[start:start + elem * a['count']]
    return b''.join(binary[start + i * stride:start + i * stride + elem] for i in range(a['count']))


def floats(doc, binary, index):
    a = doc['accessors'][index]
    raw = accessor_bytes(doc, binary, index)
    k = COMPONENTS[a['type']]
    return [struct.unpack_from('<%df' % k, raw, i * k * 4) for i in range(a['count'])]


def to_png(raw, size=1024):
    try:
        from PIL import Image
    except ImportError:
        sys.exit('Hace falta Pillow para convertir la textura a PNG: pip install pillow')
    img = Image.open(io.BytesIO(raw)).convert('RGB')
    if max(img.size) > size:
        img = img.resize((size, size), Image.LANCZOS)
    out = io.BytesIO()
    img.save(out, 'PNG', optimize=True)
    return out.getvalue()


class Writer:
    def __init__(self):
        self.doc = {'asset': {'version': '2.0', 'generator': 'CaballeroDeTinta merge_meshy.py'},
                    'accessors': [], 'bufferViews': [], 'buffers': []}
        self.bin = bytearray()

    def view(self, raw, target=None):
        while len(self.bin) % 4:
            self.bin.append(0)
        v = {'buffer': 0, 'byteOffset': len(self.bin), 'byteLength': len(raw)}
        if target:
            v['target'] = target
        self.bin += raw
        self.doc['bufferViews'].append(v)
        return len(self.doc['bufferViews']) - 1

    def copy_accessor(self, doc, binary, index, target=None):
        a = dict(doc['accessors'][index])
        a.pop('byteOffset', None)
        a.pop('sparse', None)
        a['bufferView'] = self.view(accessor_bytes(doc, binary, index), target)
        self.doc['accessors'].append(a)
        return len(self.doc['accessors']) - 1

    def save(self, path):
        while len(self.bin) % 4:
            self.bin.append(0)
        self.doc['buffers'] = [{'byteLength': len(self.bin)}]
        text = json.dumps(self.doc, separators=(',', ':')).encode()
        while len(text) % 4:
            text += b' '
        total = 12 + 8 + len(text) + 8 + len(self.bin)
        with open(path, 'wb') as f:
            f.write(struct.pack('<III', 0x46546C67, 2, total))
            f.write(struct.pack('<II', len(text), 0x4E4F534A) + text)
            f.write(struct.pack('<II', len(self.bin), 0x004E4942) + self.bin)


def main(out_path, textured_path, anim_paths):
    base, base_bin = load(anim_paths[0])
    tex, tex_bin = load(textured_path)
    w = Writer()

    # Comprueba que el modelo texturizado es la misma malla: mismas UV en el mismo orden.
    bp = base['meshes'][0]['primitives'][0]
    tp = tex['meshes'][0]['primitives'][0]
    uv_a = floats(base, base_bin, bp['attributes']['TEXCOORD_0'])
    uv_b = floats(tex, tex_bin, tp['attributes']['TEXCOORD_0'])
    if len(uv_a) != len(uv_b) or max(max(abs(x - y) for x, y in zip(p, q)) for p, q in zip(uv_a, uv_b)) > 1e-4:
        sys.exit('El modelo texturizado no tiene las mismas UV que el riggeado: no se pueden juntar.')

    # Malla: solo los atributos que usa el juego.
    attributes = {}
    for name in ('POSITION', 'NORMAL', 'TEXCOORD_0', 'JOINTS_0', 'WEIGHTS_0'):
        attributes[name] = w.copy_accessor(base, base_bin, bp['attributes'][name], 34962)
    primitive = {'attributes': attributes, 'material': 0,
                 'indices': w.copy_accessor(base, base_bin, bp['indices'], 34963)}
    w.doc['meshes'] = [{'name': 'Baldomero', 'primitives': [primitive]}]

    # Esqueleto: los nodos del archivo base tal cual.
    w.doc['nodes'] = base['nodes']
    w.doc['scenes'] = base['scenes']
    w.doc['scene'] = base.get('scene', 0)
    skin = dict(base['skins'][0])
    skin['inverseBindMatrices'] = w.copy_accessor(base, base_bin, skin['inverseBindMatrices'])
    w.doc['skins'] = [skin]

    # Material con la textura de color del modelo texturizado.
    color = tex['materials'][0]['pbrMetallicRoughness']['baseColorTexture']['index']
    image = tex['images'][tex['textures'][color]['source']]
    view = tex['bufferViews'][image['bufferView']]
    raw = tex_bin[view.get('byteOffset', 0):view.get('byteOffset', 0) + view['byteLength']]
    raw = to_png(raw)
    w.doc['images'] = [{'name': 'Baldomero', 'mimeType': 'image/png', 'bufferView': w.view(raw)}]
    w.doc['samplers'] = [{}]
    w.doc['textures'] = [{'sampler': 0, 'source': 0}]
    w.doc['materials'] = [{'name': 'Baldomero', 'doubleSided': True,
                           'pbrMetallicRoughness': {'baseColorTexture': {'index': 0}, 'metallicFactor': 0, 'roughnessFactor': 1}}]

    # Animaciones: una por archivo, con los canales apuntando a los nodos del archivo base por nombre.
    names = {n.get('name'): i for i, n in enumerate(base['nodes'])}
    w.doc['animations'] = []
    for path in anim_paths:
        doc, binary = load(path)
        for anim in doc.get('animations', []):
            if anim['name'].endswith('.001'):
                continue
            samplers, channels = [], []
            for ch in anim['channels']:
                node = doc['nodes'][ch['target']['node']].get('name')
                if node not in names:
                    continue
                s = anim['samplers'][ch['sampler']]
                samplers.append({'input': w.copy_accessor(doc, binary, s['input']),
                                 'output': w.copy_accessor(doc, binary, s['output']),
                                 'interpolation': s.get('interpolation', 'LINEAR')})
                channels.append({'sampler': len(samplers) - 1, 'target': {'node': names[node], 'path': ch['target']['path']}})
            w.doc['animations'].append({'name': anim['name'], 'samplers': samplers, 'channels': channels})
            print('  animación', anim['name'])

    w.save(out_path)
    print('Guardado', out_path, len(w.bin) // 1024, 'KB')


if __name__ == '__main__':
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2], sys.argv[3:])
