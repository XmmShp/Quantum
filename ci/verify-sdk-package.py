"""Keep the framework-internal mobile transport out of the public SDK package."""
import sys
from pathlib import Path
from zipfile import ZipFile

packages = list(Path(sys.argv[1]).glob("*.nupkg"))
if len(packages) != 1 or not packages[0].name.startswith("Quantum.Plugin.Abstraction."):
    raise ValueError("Expected exactly the public Quantum.Plugin.Abstraction package")
with ZipFile(packages[0]) as package:
    manifest = package.read("Quantum.Plugin.Abstraction.nuspec").decode("utf-8-sig")
    if "Quantum.Mobile." in manifest or any("Quantum.Mobile." in name for name in package.namelist()):
        raise ValueError("Internal mobile transport must not be published with the SDK")
print("Verified the public SDK package boundary")
