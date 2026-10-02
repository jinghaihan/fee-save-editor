"""Independent binary fixtures and byte-diff checks for roster CLI commands."""

from __future__ import annotations

import json
import struct
import subprocess
import tempfile
import zlib
from pathlib import Path


def game_hash(value: str) -> int:
    result = 2166136261
    for character in value:
        result = ((result * 16777619) & 0xffffffff) ^ ord(character)
    return result


def reference(value: str) -> bytes:
    return struct.pack("<HI", 0xefcd, game_hash(value))


def unit(person: str, job: str, has_target: bool) -> bytes:
    body = struct.pack("<II", 40, 0xcdcdcdcd) + bytes(24) + struct.pack("<Q", 0x80000000000)
    body += reference(person) + reference(job)
    body += struct.pack("<I11b", 0, 2, 3, 3, 4, 5, 6, 7, 8, 1, 0, 0)
    body += (struct.pack("<I", 0) + bytes(11)) * 2
    body += struct.pack("<IIBBB4bfB", 0x12345678, 0x87654321, 5, 12, 20, -1, -1, -1, -1, 0.0, has_target)
    if has_target:
        body += reference("PID_ヴァンドレ")
    body += struct.pack("<IIB", 0, 2, 8)
    body += struct.pack("<IB", 5, 1) + reference("IID_リカバー") + struct.pack("<BBIH", 4, 0, 0xdeadbeef, 0xccdb)
    body += struct.pack("<IB", 5, 0) * 7
    body += struct.pack("<I4Hi", 0, 0xccdb, 0xccdb, 0xccdb, 0xccdb, 0)
    body += struct.pack("<II", 1, 0) * 3 + struct.pack("<BIIII", 0, 2, 510, 2, 6)
    capability = struct.pack("<II11i", 1, 11, *([0] * 11))
    body += capability * 3 + struct.pack("<I", 1) + capability + struct.pack("<bBB", 4, 2, 0)
    body += bytes(range(48)) + struct.pack("<BBhIbb", 1, 2, 400, 0x87654321, -1, -1)
    return struct.pack("<I", len(body) + 4) + body


def fixture(base: bytes) -> bytes:
    offsets = [offset for offset in struct.unpack_from("<32I", base, 132) if offset]
    sections = [base[start:end] for start, end in zip(offsets, offsets[1:])]
    payload = struct.pack("<II", 0, 0xcdcdcdcd) + bytes(24) + bytes((3, 2))
    payload += unit("PID_リュール", "JID_神竜ノ子", False) + unit("PID_ユナカ", "JID_シーフ", True) + b"\xff"
    sections.insert(1, b"TINU" + struct.pack("<I", len(payload) + 4) + payload)
    start = 260
    positions = []
    for section in sections:
        positions.append(start)
        start += len(section)
    positions.append(start)
    index = struct.pack("<32I", *positions, *([0] * (32 - len(positions))))
    body = base[:132] + index + b"".join(sections) + b"LVRC"
    return body + struct.pack("<I", zlib.crc32(body))


def records(data: bytes) -> list[int]:
    offsets = struct.unpack_from("<32I", data, 132)
    section = next(offset for offset in offsets if offset and data[offset:offset + 4] == b"TINU")
    end = section + 4 + struct.unpack_from("<I", data, section + 4)[0]
    position = section + 8 + 32
    result = []
    while data[position] != 255:
        count = data[position + 1]
        position += 2
        for _ in range(count):
            result.append(position)
            length, version, marker = struct.unpack_from("<III", data, position)
            assert length >= 160 and version == 40 and marker == 0xcdcdcdcd
            position += length
    assert position + 1 == end
    return result


def check_roster(command: list[str], real_directory: Path | None, base: bytes) -> None:
    original = fixture(base)
    cases = [("synthetic", original)]
    if real_directory:
        cases += [(name, (real_directory / name).read_bytes()) for name in ("Auto", "Manual0")]
    with tempfile.TemporaryDirectory(prefix="fee-roster-test-") as directory:
        root = Path(directory)
        source, output, restored = (root / name for name in ("source", "edited", "restored"))

        def run(*args: str) -> subprocess.CompletedProcess:
            return subprocess.run([*command, "roster", *args], capture_output=True, text=True)

        for name, data in cases:
            source.write_bytes(data)
            listed = run("list", str(source), "--json")
            assert listed.returncode == 0, listed.stderr
            roster = json.loads(listed.stdout)
            positions = records(data)
            assert len(positions) == len(roster)
            for start, character in zip(positions, roster):
                end = start + struct.unpack_from("<I", data, start)[0]
                assert character["Values"] == dict(Level=data[start + 109], Experience=data[start + 110],
                                                   SkillPoints=struct.unpack_from("<h", data, end - 8)[0])
            chinese = run("list", str(source), "--language", "zh-Hans", "--json")
            assert chinese.returncode == 0 and json.loads(chinese.stdout)[0]["Name"] == "琉尔"
            old = roster[0]["Values"]["SkillPoints"]
            changed = 9998 if old == 9999 else 9999
            success = run("set", str(source), str(output), "--character", "0", "--sp", str(changed))
            assert success.returncode == 0, success.stderr
            edited = output.read_bytes()
            sp_offset = positions[0] + struct.unpack_from("<I", data, positions[0])[0] - 8
            allowed = {sp_offset, sp_offset + 1, *range(len(data) - 4, len(data))}
            assert len(edited) == len(data) and {i for i, pair in enumerate(zip(data, edited)) if pair[0] != pair[1]} <= allowed
            assert zlib.crc32(edited[:-4]) == struct.unpack_from("<I", edited, len(edited) - 4)[0]
            assert run("set", str(output), str(restored), "--character", "0", "--sp", str(old)).returncode == 0
            assert restored.read_bytes() == data and source.read_bytes() == data
            output.unlink()
            restored.unlink()
            print(f"{name}: roster independent scalar decoding, translation and byte-diff preservation passed.")

        source.write_bytes(original)
        catalog = json.loads(run("catalog", "--json").stdout)
        assert len(catalog["Skills"]) == 277
        canter, starsphere = "SID_再移動", "SID_星玉の加護"
        assert run("skill-unlock", str(source), str(output), "--character", "0", "--skill", canter).returncode == 0
        assert len(output.read_bytes()) == len(original) + 14
        assert run("skills-equip", str(output), str(restored), "--character", "0", "--first", canter, "--second", "none").returncode == 0
        info = json.loads(run("list", str(restored), "--json").stdout)[0]
        assert info["EquippedSkills"][0]["Name"] == "Canter"
        output.unlink()
        assert run("skill-remove", str(restored), str(output), "--character", "0", "--skill", canter).returncode == 0
        assert output.read_bytes() == original
        output.unlink()
        restored.unlink()
        assert run("skills-max", str(source), str(output), "--character", "0").returncode == 0
        info = json.loads(run("list", str(output), "--language", "zh-Hans", "--json").stdout)[0]
        assert len(info["InheritedSkills"]) == 94
        assert next(skill for skill in info["InheritedSkills"] if skill["Id"] == starsphere)["Name"] != "Starsphere"
        output.unlink()
        assert run("proficiencies", str(source), str(output), "--character", "0", "--weapons", "Sword").returncode == 0
        info = json.loads(run("list", str(output), "--json").stdout)[0]
        assert info["Progress"]["Proficiencies"] == 2
        output.unlink()
        assert run("condition", str(source), str(output), "--character", "0", "--internal-level", "-100", "--hp", "0").returncode == 0
        info = json.loads(run("list", str(output), "--json").stdout)[0]
        assert info["Progress"]["InternalLevel"] == -100 and info["Progress"]["CurrentHP"] == 0
        assert run("condition", str(output), str(restored), "--character", "0", "--internal-level", "4", "--hp", "20").returncode == 0
        assert restored.read_bytes() == original
        output.unlink()
        restored.unlink()
        result = run("class", str(source), str(output), "--character", "0", "--class", "JID_ブレイブヒーロー", "--weapons", "Sword,Axe")
        assert result.returncode == 0, result.stderr
        data = output.read_bytes()
        start = records(data)[0]
        assert struct.unpack_from("<I", data, start + 52)[0] == game_hash("JID_ブレイブヒーロー")
        assert data[start + 109:start + 111] == bytes((1, 0))
        info = json.loads(run("list", str(output), "--json").stdout)[0]
        assert info["Progress"]["SelectedWeapons"] == 10 and info["Progress"]["InternalLevel"] == 8
        assert run("set", str(output), str(restored), "--character", "0", "--level", "5").returncode == 0
        output.unlink()
        class_baseline = restored.read_bytes()
        assert run("class-skill", str(restored), str(output), "--character", "0", "--unlocked", "true").returncode == 0
        assert json.loads(run("list", str(output), "--json").stdout)[0]["Progress"]["ClassSkill"] is not None
        restored.unlink()
        assert run("class-skill", str(output), str(restored), "--character", "0", "--unlocked", "false").returncode == 0
        assert restored.read_bytes() == class_baseline
        output.unlink()
        restored.unlink()
        for character, level in (("0", "20"), ("1", "40")):
            assert run("set", str(source), str(output), "--character", character, "--level", level, "--experience", "0").returncode == 0
            output.unlink()
        assert run("stat", str(source), str(output), "--character", "0", "--stat", "Strength", "--value", "42").returncode == 0
        assert output.read_bytes()[records(original)[0] + 61] == 36
        output.unlink()
        assert run("item-set", str(source), str(output), "--character", "0", "--slot", "1", "--item", "IID_リカバー").returncode == 0
        assert len(output.read_bytes()) == len(original) + 14
        assert run("item-delete", str(output), str(restored), "--character", "0", "--slot", "1").returncode == 0
        assert restored.read_bytes() == original
        output.unlink()
        restored.unlink()
        assert run("restore", str(source), str(output), "--character", "0").returncode == 0
        first_item_uses = records(original)[0] + 141
        assert output.read_bytes()[first_item_uses] == 10
        output.unlink()
        failures = [
            ("condition", "--character", "0", "--internal-level", "101"),
            ("condition", "--character", "0", "--hp", "255"),
            ("condition", "--character", "0"),
            ("skill-unlock", "--character", "0", "--skill", "SID_missing"),
            ("skills-equip", "--character", "0", "--first", canter),
            ("skills-equip", "--character", "0"),
            ("class-skill", "--character", "0", "--unlocked", "invalid"),
            ("proficiencies", "--character", "0", "--weapons", "none"),
            ("proficiencies", "--character", "0", "--weapons", "Special"),
            ("class", "--character", "0", "--class", "JID_ダンサー"),
            ("class", "--character", "0", "--class", "JID_ランスペガサス"),
            ("class", "--character", "0", "--class", "JID_ブレイブヒーロー", "--weapons", "Sword"),
            ("class", "--character", "0", "--class", "JID_ブレイブヒーロー", "--weapons", "Sword,Sword"),
            ("class", "--character", "0", "--class", "JID_missing"),
            ("set", "--character", "0", "--level", "21"),
            ("set", "--character", "1", "--level", "41"),
            ("set", "--character", "0", "--level", "20", "--experience", "1"),
            ("set", "--character", "0", "--experience", "100"),
            ("set", "--character", "0", "--sp", "10000"),
            ("set", "--character", "0", "--sp", "-1"),
            ("set", "--character", "0", "--level", "1.5"),
            ("set", "--character", "2", "--level", "5"),
            ("set", "--character", "0", "--character", "1"),
            ("set", "--character", "0", "--unused", "1"),
            ("set", "--character", "0"),
            ("stat", "--character", "0", "--stat", "Strength", "--value", "43"),
            ("stat", "--character", "0", "--stat", "HP", "--value", "0"),
            ("stat", "--character", "0", "--stat", "Sight", "--value", "3"),
            ("stat", "--character", "0", "--stat", "1", "--value", "3"),
            ("item-set", "--character", "0", "--slot", "0", "--uses", "11"),
            ("item-set", "--character", "0", "--slot", "8", "--item", "IID_リカバー"),
            ("item-set", "--character", "0", "--slot", "1"),
            ("item-delete", "--character", "0", "--slot", "-1"),
        ]
        for verb, *options in failures:
            failure = run(verb, str(source), str(output), *options)
            assert failure.returncode == 1 and "Unhandled exception" not in failure.stderr, failure
            assert not output.exists() and source.read_bytes() == original
        for options in (("--language", "missing"), ("--json", "--json"), ("--language",)):
            assert run("list", str(source), *options).returncode == 1
        assert run("set", str(source), str(source), "--character", "0", "--sp", "500").returncode == 1
        assert source.read_bytes() == original
    if real_directory:
        for name, data in cases[1:]:
            assert (real_directory / name).read_bytes() == data
