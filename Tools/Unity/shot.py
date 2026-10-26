"""Скриншот из открытого Unity Editor через Pipeline (unity CLI).

Примеры:
  py -3 -I Tools/Unity/shot.py out.png                       # Game view (в Play — с интерфейсом)
  py -3 -I Tools/Unity/shot.py out.png --camera "Main Camera"
  py -3 -I Tools/Unity/shot.py out.png --pos 260 230 -260 --look 0 0 0 --fov 50
      (временная камера ShotCam: ставится в позицию, снимает и удаляется; сцену не меняет)
"""
import argparse
import base64
import json
import subprocess
import sys

UNITY = r"C:\Users\matvey\AppData\Local\Unity\bin\unity.exe"
PROJECT = r"C:\Users\matvey\Desktop\RandomCar\RandomCar"


def unity(*args, timeout=180):
    cmd = [UNITY, "command", *args, "--project-path", PROJECT, "--format", "json"]
    out = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", timeout=timeout)
    try:
        return json.loads(out.stdout)
    except ValueError:
        return {"success": False, "raw": out.stdout[:500] + out.stderr[:500]}


def find_image(o):
    if isinstance(o, dict):
        for v in o.values():
            r = find_image(v)
            if r:
                return r
    elif isinstance(o, str) and len(o) > 5000:
        return o
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--camera")
    ap.add_argument("--pos", nargs=3, type=float)
    ap.add_argument("--look", nargs=3, type=float)
    ap.add_argument("--fov", type=float, default=55.0)
    ap.add_argument("--width", type=int, default=1600)
    ap.add_argument("--height", type=int, default=900)
    a = ap.parse_args()

    camera = a.camera
    if a.pos:
        f = lambda v: ("%.3f" % v) + "f"
        code = (
            'var go = UnityEngine.GameObject.Find("ShotCam");'
            'if (go == null) { go = new UnityEngine.GameObject("ShotCam"); go.AddComponent<UnityEngine.Camera>(); }'
            'var c = go.GetComponent<UnityEngine.Camera>(); c.depth = -50; c.farClipPlane = 3000f; c.fieldOfView = %s;'
            'var d = go.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() ?? go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();'
            'd.renderPostProcessing = true;'
            'go.transform.position = new UnityEngine.Vector3(%s, %s, %s);'
            'go.transform.LookAt(new UnityEngine.Vector3(%s, %s, %s)); return "ok";'
        ) % (f(a.fov), *map(f, a.pos), *map(f, a.look or (0, 0, 0)))
        r = unity("script", "eval", code)
        if "ok" not in json.dumps(r):
            print("camera setup failed:", json.dumps(r, ensure_ascii=False)[:800])
            sys.exit(1)
        camera = "ShotCam"

    args = ["capture", "game_view", "--include_inline_image", "true", "--width", str(a.width), "--height", str(a.height)]
    if camera:
        args += ["--source", "camera", "--camera", camera]
    r = unity(*args)

    if a.pos:
        unity("script", "eval", 'var go = UnityEngine.GameObject.Find("ShotCam"); if (go != null) UnityEngine.Object.DestroyImmediate(go); return "removed";')

    img = find_image(r)
    if not img:
        print("NO IMAGE:", json.dumps(r, ensure_ascii=False)[:800])
        sys.exit(1)
    with open(a.out, "wb") as fh:
        fh.write(base64.b64decode(img.split(",")[-1]))
    print("saved", a.out)


if __name__ == "__main__":
    main()
