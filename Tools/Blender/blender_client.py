"""Отправляет Python-код в Blender через сокет BlenderMCP (127.0.0.1:9876) и печатает ответ.

Использование: py -3 -I blender_client.py <file1.py> [file2.py ...] [--timeout 600]
Файлы склеиваются по порядку (обычно rc_lib.py + конкретный скрипт) и выполняются
внутри Blender командой execute_code.
"""
import json
import socket
import sys


def send(command, timeout):
    with socket.create_connection(("127.0.0.1", 9876), timeout=10) as s:
        s.sendall(json.dumps(command).encode("utf-8"))
        s.settimeout(timeout)
        data = b""
        while True:
            chunk = s.recv(1 << 20)
            if not chunk:
                break
            data += chunk
            try:
                return json.loads(data.decode("utf-8"))
            except ValueError:
                continue
    return json.loads(data.decode("utf-8"))


def main():
    args = sys.argv[1:]
    timeout = 600.0
    if "--timeout" in args:
        i = args.index("--timeout")
        timeout = float(args[i + 1])
        del args[i:i + 2]

    parts = []
    for path in args:
        with open(path, encoding="utf-8") as f:
            parts.append(f.read())
    code = "\n\n".join(parts)

    reply = send({"type": "execute_code", "params": {"code": code}}, timeout)
    result = reply.get("result")
    if isinstance(result, dict) and "result" in result:
        print(result["result"])
    else:
        print(json.dumps(reply, ensure_ascii=False, indent=2)[:6000])
    if reply.get("status") != "success":
        sys.exit(1)


if __name__ == "__main__":
    main()
