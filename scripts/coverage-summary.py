#!/usr/bin/env python3
"""Render a Cobertura report as a GitHub Actions job summary.

Coverage is measured and published for visibility only; it is not enforced by
the `main` branch ruleset. See AGENTS.md, "Merge Gates on `main`".
"""

import sys
import xml.etree.ElementTree as ET


def pct(value: str | None) -> str:
    return f"{float(value or 0) * 100:.2f}%"


def main(path: str) -> int:
    root = ET.parse(path).getroot()

    print("## Code coverage\n")
    print("| Metric | Covered | Total | Rate |")
    print("| --- | ---: | ---: | ---: |")
    for label, covered, valid, rate in (
        ("Lines", "lines-covered", "lines-valid", "line-rate"),
        ("Branches", "branches-covered", "branches-valid", "branch-rate"),
    ):
        print(
            f"| {label} | {root.get(covered, '0')} | {root.get(valid, '0')} "
            f"| {pct(root.get(rate))} |"
        )

    packages = root.findall("./packages/package")
    if packages:
        print("\n### Per assembly\n")
        print("| Assembly | Line rate |")
        print("| --- | ---: |")
        for package in packages:
            print(f"| {package.get('name')} | {pct(package.get('line-rate'))} |")

    print(
        "\nOnly assemblies loaded by the test run appear above; the full Cobertura "
        "report is attached to this run as the `coverage-cobertura` artifact."
    )
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(f"usage: {sys.argv[0]} <coverage.cobertura.xml>", file=sys.stderr)
        raise SystemExit(2)
    raise SystemExit(main(sys.argv[1]))
