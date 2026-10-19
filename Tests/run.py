#!/usr/bin/env python3
"""Run regression tests using disposable configuration and saves, without deploying mods."""
import argparse
from contextlib import nullcontext
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-dir", required=True, type=Path)
    parser.add_argument("--source-root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--features-only", action="store_true", help="Skip the broad attack matrix; retain controls, presets, status, cloak and the focused drill regression")
    parser.add_argument("--full-matrix", action="store_true", help="Exercise all 49 pairwise mixed rows per crest; default uses 7 rotations")
    parser.add_argument("--keep-workspace", action="store_true", help="Keep disposable files in /tmp for investigation")
    parser.add_argument("--timeout", type=int, default=900)
    args = parser.parse_args()
    game = args.game_dir.expanduser().resolve()
    source = args.source_root.resolve()
    context = nullcontext(tempfile.mkdtemp(prefix="silksong-regression-")) if args.keep_workspace else tempfile.TemporaryDirectory(prefix="silksong-regression-")
    with context as directory:
        work = Path(directory)
        if args.keep_workspace: print("Test workspace: " + str(work), flush=True)
        env = dict(os.environ, DOTNET_CLI_HOME=str(work / "cli"), NUGET_PACKAGES=str(work / "nuget"),
                   DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1")
        shared = source / "SaveScopedConfig/bin/Release/netstandard2.1/SaveScopedConfig.dll"
        for name in ["SaveScopedConfig", "AbilityToggles", "AutoMap", "EquipAllTools", "CrestLoadouts"]:
            command = [
                "python3", str(source / name / "build.py"), "--game-dir", str(game), "--dotnet", args.dotnet]
            if name != "SaveScopedConfig": command += ["--save-scoped-dll", str(shared)]
            subprocess.run(command, env=env, check=True)
        subprocess.run([args.dotnet, "run", "--project", str(source / "SaveScopedConfig/Tests/CoreTests.csproj"), "-c", "Release"], env=env, check=True)
        shutil.copytree(game / "BepInEx", work / "BepInEx", ignore=shutil.ignore_patterns("cache", "LogOutput.log"))
        for name in ["run_bepinex.sh", "libdoorstop.so", ".doorstop_version"]:
            if (game / name).exists(): shutil.copy2(game / name, work / name)
        for name in ["SaveScopedConfig", "AbilityToggles", "AutoMap", "EquipAllTools", "CrestLoadouts"]:
            for old in (work / "BepInEx/plugins").rglob(name + ".dll"): old.unlink()
            target = work / "BepInEx/plugins" / name
            target.mkdir(exist_ok=True)
            shutil.copy2(source / name / "bin/Release/netstandard2.1" / (name + ".dll"), target / (name + ".dll"))
        testdir = work / "BepInEx/plugins/Regression"
        subprocess.run([args.dotnet, "build", str(Path(__file__).with_name("Regression.csproj")), "-c", "Release", "--nologo",
                        "-p:GameDir=" + str(game), "-p:SaveScopedConfigPath=" + str(shared), "-o", str(testdir)], env=env, check=True)
        env.update(XDG_CONFIG_HOME=str(work / "userconfig"), SILKSONG_MOD_TEST_ROOT=str(work),
                   SILKSONG_MOD_TEST_FULL="1" if args.full_matrix else "0",
                   SILKSONG_MOD_TEST_FEATURES_ONLY="1" if args.features_only else "0", SteamAppId="1030300", SteamGameId="1030300")
        with (work / "player-output.txt").open("w") as output:
            process = subprocess.Popen([str(work / "run_bepinex.sh"), str(game / "Hollow Knight Silksong"),
                                        "-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720"],
                                       cwd=game, env=env, stdout=output, stderr=subprocess.STDOUT)
            try:
                deadline = time.monotonic() + args.timeout
                while time.monotonic() < deadline and not (work / "result.txt").exists() and process.poll() is None:
                    time.sleep(1)
                result = (work / "result.txt").read_text() if (work / "result.txt").exists() else "FAIL: game exited or testing timed out"
                print(result)
                if not result.startswith("PASS"):
                    log = work / "BepInEx/LogOutput.log"
                    if log.exists(): print("\n".join(log.read_text(errors="replace").splitlines()[-60:]))
                    raise SystemExit(1)
            finally:
                if process.poll() is None:
                    process.terminate()
                    try: process.wait(timeout=5)
                    except subprocess.TimeoutExpired: process.kill(); process.wait()
                subprocess.run([args.dotnet, "build-server", "shutdown"], env=env, check=False)


if __name__ == "__main__": main()
