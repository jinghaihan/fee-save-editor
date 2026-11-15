"""Independent synthetic ChapterRecord v2 fixture; no private save data."""
import struct
import zlib
from pathlib import Path


def fixture(base):
    def string(value):
        if value is None:
            return struct.pack('<I', 0xffffffff)
        data = value.encode('utf-16-le')
        return struct.pack('<I', len(data)) + data
    rows = [
        ('CID_M000', 'M000_リュール', 'M000_神竜ノ子', 'M000_マルス', 42.125, 1),
        ('CID_M014', 'パネトネ', 'ウォーリアー', 'アイク', 5341.7890625, 34),
        ('CID_E006', 'パネトネ', 'ベルセルク', 'クロード', 308.5, 1),
        ('CID_G001', 'エーティエ', 'アーチャー', None, 3659.25, 52),
    ]
    block = struct.pack('<II', 2, 0xcdcdcdcd) + bytes(24) + bytes([len(rows)])
    for *ids, seconds, turns in rows:
        block += b''.join(string(value) for value in ids) + struct.pack('<fi', seconds, turns)
    block = struct.pack('<I', len(block) + 4) + block
    offsets = [v for v in struct.unpack_from('<32I', base, 132) if v]
    sections = []
    for start, end in zip(offsets, offsets[1:]):
        section = base[start:end]
        if section[:4] == b'RESU':
            payload = section[8:] + block + b'untouched-user-suffix'
            section = b'RESU' + struct.pack('<I', len(payload) + 4) + payload
        sections.append(section)
    # A rewind snapshot with matching CID strings must never be edited.
    payload = b'untouched-rewind-prefix' + block
    sections.append(b'TSER' + struct.pack('<I', len(payload) + 4) + payload)
    body = bytearray(base[:128] + b'EDNI' + bytes(128))
    for i, section in enumerate(sections):
        struct.pack_into('<I', body, 132+i*4, len(body))
        body += section
    struct.pack_into('<I', body, 132+len(sections)*4, len(body))
    body += b'LVRC'
    return body + struct.pack('<I', zlib.crc32(body))


if __name__ == '__main__':
    root = Path(__file__).resolve().parents[1] / 'internal/save/testdata'
    (root/'chapter-records.bin').write_bytes(fixture((root/'creation.bin').read_bytes()))
