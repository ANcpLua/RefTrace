"""Check the severities in an InspectCode SARIF report."""

from collections import Counter
import json
from pathlib import Path
import sys


def rule_id(identifier, index, rules):
    if index == -1:
        return identifier
    if not 0 <= index < len(rules):
        raise ValueError(f"invalid rule index {index}")
    indexed_id = rules[index]["id"]
    if identifier is not None and indexed_id != identifier:
        raise ValueError(f"rule index {index} disagrees with rule ID {identifier!r}")
    return indexed_id


def effective_level(result, identifier, defaults, rules, invocations):
    if "level" in result:
        return result["level"]
    if result.get("kind", "fail") != "fail":
        return "none"
    index = result.get("provenance", {}).get("invocationIndex", 0 if len(invocations) == 1 else -1)
    if index != -1:
        if not 0 <= index < len(invocations):
            raise ValueError(f"invalid invocation index {index}")
        for override in invocations[index].get("ruleConfigurationOverrides", []):
            descriptor = override["descriptor"]
            override_id = rule_id(descriptor.get("id"), descriptor.get("index", -1), rules)
            if override_id == identifier and "level" in override["configuration"]:
                return override["configuration"]["level"]
    return defaults.get(identifier, "warning")


def check(report):
    if report["version"] != "2.1.0" or not isinstance(report["runs"], list) or not report["runs"]:
        raise ValueError("expected a SARIF 2.1.0 report containing an inspection run")
    counts = Counter()
    for run in report["runs"]:
        invocations = run.get("invocations", [])
        if any(invocation["executionSuccessful"] is not True for invocation in invocations):
            raise ValueError("InspectCode reported an unsuccessful invocation")
        rules = run["tool"]["driver"].get("rules", [])
        defaults = {rule["id"]: rule.get("defaultConfiguration", {}).get("level", "warning") for rule in rules}
        if not isinstance(run["results"], list):
            raise ValueError("expected an InspectCode results array")
        for result in run["results"]:
            descriptor = result.get("rule", {})
            identifier = rule_id(descriptor.get("id", result.get("ruleId")),
                                 descriptor.get("index", result.get("ruleIndex", -1)), rules)
            level = effective_level(result, identifier, defaults, rules, invocations)
            if level not in {"error", "warning", "note", "none"}:
                raise ValueError(f"invalid finding severity {level!r}")
            counts[level] += 1
            locations = result.get("locations", [])
            physical = locations[0].get("physicalLocation", {}) if locations else {}
            uri = physical.get("artifactLocation", {}).get("uri", "")
            line = physical.get("region", {}).get("startLine")
            where = f"{uri}:{line}" if line else uri
            message = result["message"]
            detail = message.get("text") or message.get("markdown") or message["id"]
            print(" ".join(part for part in (where, level, identifier, detail) if part))
    print(f"InspectCode totals: errors={counts['error']}, warnings={counts['warning']}, notes={counts['note']}")
    return 1 if counts["error"] or counts["warning"] else 0


if __name__ == "__main__":
    try:
        with Path(sys.argv[1]).open(encoding="utf-8-sig") as source:
            sys.exit(check(json.load(source)))
    except (OSError, ValueError, KeyError, IndexError, TypeError, AttributeError) as error:
        sys.exit(f"Cannot check InspectCode report: {error}")
