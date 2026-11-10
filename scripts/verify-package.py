"""Check release provenance and actual NuGet contents using only Python's standard library."""

import argparse
import io
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

PACKAGE_ID = "Umbraco.Community.MemberCache"
PROJECT = Path("src") / PACKAGE_ID / f"{PACKAGE_ID}.csproj"
SCHEMA = f"appsettings-schema.{PACKAGE_ID}.json"
TARGETS = f"buildTransitive/{PACKAGE_ID}.targets"
NUMBER = r"(?:0|[1-9][0-9]*)"
IDENTIFIER = rf"(?:{NUMBER}|[0-9]*[A-Za-z-][0-9A-Za-z-]*)"
SEMVER = re.compile(
    rf"(?P<major>{NUMBER})\.{NUMBER}\.{NUMBER}"
    rf"(?:-{IDENTIFIER}(?:\.{IDENTIFIER})*)?"
    r"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?"
)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def parse_version(version):
    match = SEMVER.fullmatch(version)
    require(match is not None, f"Invalid whole SemVer: {version!r}")
    return int(match["major"])


def local_name(element):
    return element.tag.rsplit("}", 1)[-1]


def elements(root, name):
    return [node for node in root.iter() if local_name(node) == name]


def project_contract():
    project = ET.parse(PROJECT).getroot()
    versions = elements(project, "Version")
    require(len(versions) == 1, "Project must declare one explicit Version")
    version = versions[0].text
    major = parse_version(version)
    require(major in (17, 18), "Supported package majors are 17 and 18")
    frameworks = elements(project, "TargetFramework")
    require(len(frameworks) == 1 and frameworks[0].text == "net10.0", "Expected net10.0")
    references = [node for node in elements(project, "PackageReference")
                  if node.get("Include", "").startswith("Umbraco.Cms.")]
    require(references, "Missing explicit Umbraco dependency")
    interval = f"[{major}.0.0,{major + 1}.0.0)"
    require(all(node.get("Version") == interval for node in references),
            f"Every direct Umbraco dependency must use {interval}")
    return version, major, interval


def verify_release(tag):
    require(tag.startswith("v"), "Release tag must begin with v")
    version = tag[1:]
    major = parse_version(version)
    _, project_major, _ = project_contract()
    require(major == project_major, "Tag major does not match this branch's project")
    branch = "main" if major == 18 else "v17/main"
    subprocess.run(["git", "fetch", "--no-tags", "origin",
                    f"+refs/heads/{branch}:refs/remotes/origin/{branch}"], check=True)
    commit = subprocess.check_output(["git", "rev-parse", f"refs/tags/{tag}^{{commit}}"], text=True).strip()
    head = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    require(commit == head, "Checked-out commit is not the tagged commit")
    subprocess.run(["git", "merge-base", "--is-ancestor", commit,
                    f"refs/remotes/origin/{branch}"], check=True)
    output = os.environ.get("GITHUB_OUTPUT")
    if output:
        with open(output, "a", encoding="utf-8") as stream:
            stream.write(f"version={version}\nbranch={branch}\n")
    print(f"Verified {tag} at {commit} on origin/{branch}")


def verify_archive(archive, version, major, interval):
    names = archive.namelist()
    require(len(names) == len(set(names)), "Duplicate archive entries")
    names = [name for name in names if not name.endswith("/")]
    required = {f"{PACKAGE_ID}.nuspec", "README.md", "icon.png", SCHEMA, TARGETS,
                f"lib/net10.0/{PACKAGE_ID}.dll"}
    require(required <= set(names), f"Missing required files: {sorted(required - set(names))}")
    allowed = required | {"[Content_Types].xml", "_rels/.rels", "LICENSE", "LICENSE.txt",
                          f"lib/net10.0/{PACKAGE_ID}.xml", "package/services/metadata/core-properties/nuget.psmdcp"}
    unexpected = [name for name in names if name not in allowed and not re.fullmatch(
        r"package/services/metadata/core-properties/[0-9a-f]+\.psmdcp", name)]
    require(not unexpected, f"Unexpected package content, including possible demo, credentials or raw sources: {unexpected}")
    nuspec = ET.fromstring(archive.read(f"{PACKAGE_ID}.nuspec"))

    def value(name):
        nodes = elements(nuspec, name)
        require(len(nodes) == 1, f"Expected exactly one nuspec {name}")
        return nodes[0].text

    require(value("id") == PACKAGE_ID, "Incorrect NuGet ID")
    actual_version = value("version")
    require(parse_version(actual_version) == major, "Incorrect NuGet major")
    # NuGet drops SemVer build metadata from package identity.
    require(actual_version.split("+", 1)[0] == version.split("+", 1)[0], "Incorrect NuGet version")
    require(value("readme") == "README.md" and value("icon") == "icon.png", "Incorrect readme/icon metadata")
    licenses = elements(nuspec, "license")
    require(len(licenses) == 1 and licenses[0].get("type") == "expression"
            and licenses[0].text == "MIT", "Expected MIT license expression")
    dependency_sections = elements(nuspec, "dependencies")
    require(len(dependency_sections) == 1, "Expected one dependency section")
    groups = elements(dependency_sections[0], "group")
    require(len(groups) == 1 and groups[0].get("targetFramework") == "net10.0", "Incorrect dependency TFM")
    dependencies = [node for node in elements(nuspec, "dependency")
                    if node.get("id", "").startswith("Umbraco.Cms.")]
    require(dependencies and all(node.get("version", "").replace(" ", "") == interval
                                 for node in dependencies), "Incorrect Umbraco dependency interval")
    require(archive.read(f"lib/net10.0/{PACKAGE_ID}.dll").startswith(b"MZ"), "Invalid DLL header")
    require(bool(archive.read("README.md").decode("utf-8").strip()), "Empty README")
    icon = archive.read("icon.png")
    require(icon.startswith(b"\x89PNG\r\n\x1a\n") and len(icon) >= 24 and len(icon) <= 1_000_000, "Invalid PNG icon")
    width, height = struct.unpack(">II", icon[16:24])
    require(width > 0 and height > 0, "Invalid icon dimensions")
    schema = json.loads(archive.read(SCHEMA))
    options = schema["properties"]["Umbraco"]["properties"]["Community"]["properties"]["MemberCache"]["properties"]
    require(set(options) == {"MaxModels", "LocalCacheExpiration", "CacheExpiration", "Seed"}, "Unexpected schema settings")
    target = ET.fromstring(archive.read(TARGETS))
    items = elements(target, "UmbracoJsonSchemaFiles")
    require(len(items) == 1 and items[0].get("Include", "").replace("\\", "/")
            == f"$(MSBuildThisFileDirectory)../{SCHEMA}", "Incorrect schema registration target")


def verify_package(path, expected_version=None):
    version, major, interval = project_contract()
    version = expected_version or version
    require(parse_version(version) == major, "Expected version major does not match project")
    packages = list(path.glob("*.nupkg")) if path.is_dir() else [path]
    require(len(packages) == 1, "Expected exactly one .nupkg")
    with zipfile.ZipFile(packages[0]) as archive:
        verify_archive(archive, version, major, interval)
    print(f"Verified {packages[0]} ({version}, net10.0, {interval})")


def self_test():
    for version in ("18.0.0", "17.0.1-beta.2", "18.1.0-rc1", "18.0.0-0", "18.0.0+build.01"):
        parse_version(version)
    for version in ("018.0.0", "18.00.0", "18.0.01", "18.0.0-01", "18.0.0-beta.01",
                    "18.0.0-", "18.0.0+", "18.0.0 trailing", "18.0.0\n", "v18.0.0"):
        try:
            parse_version(version)
        except ValueError:
            continue
        raise AssertionError(f"Accepted invalid version {version!r}")
    schema = Path("src", PACKAGE_ID, SCHEMA).read_bytes()
    targets = Path("src", PACKAGE_ID, TARGETS).read_bytes()
    fixture = {
        f"{PACKAGE_ID}.nuspec": f'''<package><metadata><id>{PACKAGE_ID}</id><version>18.0.0</version>
            <readme>README.md</readme><icon>icon.png</icon><license type="expression">MIT</license>
            <dependencies><group targetFramework="net10.0"><dependency id="Umbraco.Cms.Core"
            version="[18.0.0,19.0.0)" /></group></dependencies>
            <frameworkReferences><group targetFramework="net10.0"><frameworkReference name="Microsoft.AspNetCore.App" /></group></frameworkReferences>
            </metadata></package>'''.encode(),
        "README.md": b"Member Cache", "icon.png": b"\x89PNG\r\n\x1a\n" + b"\0" * 8 + struct.pack(">II", 128, 128),
        SCHEMA: schema, TARGETS: targets, f"lib/net10.0/{PACKAGE_ID}.dll": b"MZfixture",
    }

    def check(entries):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            for name, content in entries.items():
                archive.writestr(name, content)
        stream.seek(0)
        with zipfile.ZipFile(stream) as archive:
            verify_archive(archive, "18.0.0", 18, "[18.0.0,19.0.0)")

    check(fixture)
    check({**fixture, "package/services/metadata/core-properties/nuget.psmdcp": b"<coreProperties />"})
    for description, mutation in (
        ("missing DLL", {key: value for key, value in fixture.items() if not key.endswith(".dll")}),
        ("wrong package ID", {**fixture, f"{PACKAGE_ID}.nuspec": fixture[f"{PACKAGE_ID}.nuspec"].replace(f"<id>{PACKAGE_ID}".encode(), b"<id>Wrong.Package")}),
        ("wrong TFM", {**fixture, f"{PACKAGE_ID}.nuspec": fixture[f"{PACKAGE_ID}.nuspec"].replace(b"net10.0", b"net9.0")}),
        ("missing icon", {key: value for key, value in fixture.items() if key != "icon.png"}),
        ("missing README", {key: value for key, value in fixture.items() if key != "README.md"}),
        ("credentials", {**fixture, "demo/appsettings.json": b'{"Password":"secret"}'}),
        ("raw sources", {**fixture, "src/MemberCache.cs": b"class MemberCache {}"}),
        ("arbitrary metadata file", {**fixture, "package/services/metadata/core-properties/credentials.psmdcp": b"secret"}),
        ("wrong interval", {**fixture, f"{PACKAGE_ID}.nuspec": fixture[f"{PACKAGE_ID}.nuspec"].replace(b"[18.0.0,19.0.0)", b"18.0.0")}),
        ("wrong version", {**fixture, f"{PACKAGE_ID}.nuspec": fixture[f"{PACKAGE_ID}.nuspec"].replace(b"<version>18.0.0", b"<version>18.0.1")}),
        ("wrong schema target", {**fixture, TARGETS: targets.replace(b"UmbracoJsonSchemaFiles", b"WrongItem")}),
    ):
        try:
            check(mutation)
        except ValueError:
            continue
        raise AssertionError(f"Accepted {description}")
    print("Self-check passed: whole SemVer and package boundary rejection cases")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    package = commands.add_parser("package")
    package.add_argument("path", type=Path)
    package.add_argument("--version")
    release = commands.add_parser("release")
    release.add_argument("tag")
    commands.add_parser("self-test")
    args = parser.parse_args()
    if args.command == "self-test":
        self_test()
    elif args.command == "release":
        verify_release(args.tag)
    else:
        verify_package(args.path, args.version)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, ET.ParseError, OSError, subprocess.CalledProcessError, zipfile.BadZipFile) as error:
        sys.exit(f"Verification failed: {error}")
