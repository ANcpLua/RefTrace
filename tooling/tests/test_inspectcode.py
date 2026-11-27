import contextlib
import io
import unittest

from tooling.check_inspectcode import check


class InspectCodeTests(unittest.TestCase):
    def setUp(self):
        self.run = {
            "tool": {"driver": {"name": "InspectCode", "rules": [
                {"id": "Style", "defaultConfiguration": {"level": "note"}}
            ]}},
            "results": [],
            "invocations": [{"executionSuccessful": True}],
        }
        self.report = {"version": "2.1.0", "runs": [self.run]}

    def check_result(self, **result):
        self.run["results"] = [{"message": {"text": "test finding"}, **result}]
        with contextlib.redirect_stdout(io.StringIO()):
            return check(self.report)

    def test_only_warnings_and_errors_fail(self):
        for level, expected in [("error", 1), ("warning", 1), ("note", 0), ("none", 0)]:
            with self.subTest(level=level):
                self.assertEqual(expected, self.check_result(level=level))

    def test_missing_level_uses_rule_default_then_sarif_default(self):
        self.assertEqual(0, self.check_result(ruleIndex=0))
        self.assertEqual(0, self.check_result(ruleId="Style"))
        self.assertEqual(1, self.check_result(ruleId="Other"))
        self.assertEqual(0, self.check_result(kind="informational"))

    def test_invocation_can_raise_a_suggestion_and_result_can_lower_it(self):
        self.run["invocations"][0]["ruleConfigurationOverrides"] = [
            {"descriptor": {"index": 0}, "configuration": {"level": "warning"}}
        ]
        self.assertEqual(1, self.check_result(ruleId="Style"))
        self.assertEqual(0, self.check_result(ruleId="Style", level="note"))

    def test_incomplete_inspection_cannot_pass(self):
        self.run["invocations"][0]["executionSuccessful"] = False
        with self.assertRaises(ValueError):
            check(self.report)
        self.assertRaises(ValueError, check, {"version": "2.1.0", "runs": []})
